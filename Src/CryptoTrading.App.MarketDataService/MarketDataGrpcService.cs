using System;
using System.Linq;
using System.Threading.Tasks;
using CryptoTrading.App.Persistence;
using CryptoTrading.App.ServiceContracts;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.MarketDataService
{
    public class MarketDataGrpcService : CryptoTrading.App.ServiceContracts.MarketDataService.MarketDataServiceBase
    {
        private readonly CryptoDbContextPg _db;
        private readonly ExchangeProviderRegistry _registry;
        private readonly CandleCacheWorker _cacheWorker;
        private readonly ILogger<MarketDataGrpcService> _logger;

        public MarketDataGrpcService(
            CryptoDbContextPg db,
            ExchangeProviderRegistry registry,
            CandleCacheWorker cacheWorker,
            ILogger<MarketDataGrpcService> logger)
        {
            _db = db;
            _registry = registry;
            _cacheWorker = cacheWorker;
            _logger = logger;
        }

        public override async Task<GetCandlesResponse> GetCandles(GetCandlesRequest request, ServerCallContext context)
        {
            var interval = CandleIntervalHelper.Parse(request.Interval);
            var intervalInt = (int)interval;
            var from = request.From.ToDateTime();
            var to = request.To.ToDateTime();

            var candles = await _db.Candlesticks
                .Where(c => c.ExchangeId == request.ExchangeId
                         && c.Symbol == request.Symbol
                         && c.Interval == intervalInt
                         && c.OpenTime >= from
                         && c.OpenTime <= to)
                .OrderBy(c => c.OpenTime)
                .Select(c => new Candle
                {
                    ExchangeId = c.ExchangeId,
                    Symbol = c.Symbol,
                    Interval = request.Interval,
                    OpenTime = Timestamp.FromDateTime(DateTime.SpecifyKind(c.OpenTime, DateTimeKind.Utc)),
                    CloseTime = Timestamp.FromDateTime(DateTime.SpecifyKind(c.CloseTime, DateTimeKind.Utc)),
                    Open = c.Open,
                    High = c.High,
                    Low = c.Low,
                    Close = c.Close,
                    Volume = (double)c.Volume,
                    QuoteVolume = (double)c.QuoteVolume,
                    NumberOfTrades = c.NumberOfTrades,
                    IsClosed = true
                })
                .ToListAsync(context.CancellationToken);

            return new GetCandlesResponse { Candles = { candles } };
        }

        public override async Task SubscribeCandles(
            SubscribeCandlesRequest request,
            IServerStreamWriter<ServiceContracts.CandleUpdate> responseStream,
            ServerCallContext context)
        {
            var reader = _cacheWorker.Subscribe(request.ExchangeId, request.Symbol, request.Interval);

            try
            {
                while (await reader.WaitToReadAsync(context.CancellationToken))
                {
                    while (reader.TryRead(out var update))
                    {
                        var candle = update.Candle;
                        var grpcUpdate = new ServiceContracts.CandleUpdate
                        {
                            Candle = new Candle
                            {
                                ExchangeId = update.ExchangeId,
                                Symbol = candle.Symbol,
                                Interval = request.Interval,
                                OpenTime = Timestamp.FromDateTime(DateTime.SpecifyKind(candle.OpenTime, DateTimeKind.Utc)),
                                CloseTime = Timestamp.FromDateTime(DateTime.SpecifyKind(candle.CloseTime, DateTimeKind.Utc)),
                                Open = (double)candle.Open,
                                High = (double)candle.High,
                                Low = (double)candle.Low,
                                Close = (double)candle.Close,
                                Volume = (double)candle.Volume,
                                QuoteVolume = (double)candle.QuoteVolume,
                                NumberOfTrades = candle.NumberOfTrades,
                                IsClosed = candle.IsClosed
                            },
                            EventTime = Timestamp.FromDateTime(DateTime.SpecifyKind(update.EventTime, DateTimeKind.Utc))
                        };

                        await responseStream.WriteAsync(grpcUpdate);
                    }
                }
            }
            finally
            {
                _cacheWorker.Unsubscribe(reader);
            }
        }

        public override async Task<GetSymbolsResponse> GetSymbols(GetSymbolsRequest request, ServerCallContext context)
        {
            var provider = _registry.Get(request.ExchangeId);
            var symbols = await provider.GetSymbolsAsync();

            var response = new GetSymbolsResponse();
            foreach (var s in symbols)
            {
                if (!string.IsNullOrEmpty(request.QuoteAssetFilter) &&
                    !string.Equals(s.QuoteAsset, request.QuoteAssetFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                response.Symbols.Add(new SymbolInfo
                {
                    ExchangeId = s.ExchangeId ?? request.ExchangeId,
                    Ticker = s.Ticker,
                    BaseAsset = s.BaseAsset,
                    QuoteAsset = s.QuoteAsset,
                    MinQuantity = (double)s.MinQuantity,
                    MaxQuantity = (double)s.MaxQuantity,
                    StepSize = (double)s.StepSize,
                    MinNotional = (double)s.MinNotional,
                    TickSize = (double)s.TickSize,
                    IsActive = s.IsActive
                });
            }

            return response;
        }

        public override Task<ServiceStatusResponse> GetServiceStatus(ServiceStatusRequest request, ServerCallContext context)
        {
            var candleCount = _db.Candlesticks.LongCount();
            var response = new ServiceStatusResponse
            {
                ActiveSubscriptions = _cacheWorker.ActiveSubscriptionCount,
                CachedCandleCount = candleCount,
                ActiveExchanges = { _registry.GetRegisteredExchanges() }
            };
            return Task.FromResult(response);
        }
    }
}
