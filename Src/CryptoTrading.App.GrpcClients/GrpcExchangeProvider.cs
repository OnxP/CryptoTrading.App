using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.ServiceContracts;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using BrokerClient = CryptoTrading.App.ServiceContracts.BrokerService.BrokerServiceClient;
using MarketDataClient = CryptoTrading.App.ServiceContracts.MarketDataService.MarketDataServiceClient;
using Enum = System.Enum;

namespace CryptoTrading.App.GrpcClients
{
    public class GrpcExchangeProvider : IExchangeProvider
    {
        private readonly BrokerClient _broker;
        private readonly MarketDataClient _marketData;
        private readonly string _exchangeId;
        private readonly TradingVenue _venue;
        private CancellationTokenSource _streamCts;

        public GrpcExchangeProvider(BrokerClient broker, MarketDataClient marketData, string exchangeId, TradingVenue venue)
        {
            _broker = broker;
            _marketData = marketData;
            _exchangeId = exchangeId;
            _venue = venue;
        }

        public string ExchangeId => _exchangeId;
        public TradingVenue Venue => _venue;

        public async Task<IEnumerable<ExchangeBalance>> GetBalancesAsync()
        {
            var response = await _broker.GetBalancesAsync(new GetBalancesRequest { ExchangeId = _exchangeId });
            return response.Balances.Select(b => new ExchangeBalance
            {
                ExchangeId = b.ExchangeId,
                Asset = b.Asset,
                Free = (decimal)b.Free,
                Locked = (decimal)b.Locked
            });
        }

        public async Task<IEnumerable<ExchangeSymbol>> GetSymbolsAsync()
        {
            var response = await _marketData.GetSymbolsAsync(new GetSymbolsRequest { ExchangeId = _exchangeId });
            return response.Symbols.Select(s => new ExchangeSymbol
            {
                ExchangeId = s.ExchangeId,
                Ticker = s.Ticker,
                BaseAsset = s.BaseAsset,
                QuoteAsset = s.QuoteAsset,
                MinQuantity = (decimal)s.MinQuantity,
                MaxQuantity = (decimal)s.MaxQuantity,
                StepSize = (decimal)s.StepSize,
                MinNotional = (decimal)s.MinNotional,
                TickSize = (decimal)s.TickSize,
                IsActive = s.IsActive
            });
        }

        public async Task<ExchangeFeeSchedule> GetFeeScheduleAsync()
        {
            var response = await _broker.GetFeeScheduleAsync(new GetFeeScheduleRequest { ExchangeId = _exchangeId });
            return new ExchangeFeeSchedule
            {
                ExchangeId = response.ExchangeId,
                MakerFee = (decimal)response.MakerFee,
                TakerFee = (decimal)response.TakerFee,
                FeeAsset = response.FeeAsset,
                HasFeeDiscount = response.HasFeeDiscount,
                DiscountRate = (decimal)response.DiscountRate
            };
        }

        public async Task<ExchangeOrder> PlaceMarketOrderAsync(
            string symbol, ExchangeOrderSide side, decimal quantity,
            PositionSide positionSide = PositionSide.Both,
            MarginSideEffect marginSideEffect = MarginSideEffect.None,
            bool reduceOnly = false)
        {
            var response = await _broker.PlaceMarketOrderAsync(new PlaceMarketOrderRequest
            {
                ExchangeId = _exchangeId,
                Symbol = symbol,
                Side = side.ToString(),
                Quantity = (double)quantity,
                Venue = _venue.ToString(),
                PositionSide = positionSide.ToString(),
                MarginSideEffect = marginSideEffect.ToString(),
                ReduceOnly = reduceOnly
            });
            return MapOrder(response);
        }

        public async Task<ExchangeOrder> PlaceLimitOrderAsync(
            string symbol, ExchangeOrderSide side, decimal price, decimal quantity,
            PositionSide positionSide = PositionSide.Both,
            MarginSideEffect marginSideEffect = MarginSideEffect.None,
            bool reduceOnly = false)
        {
            var response = await _broker.PlaceLimitOrderAsync(new PlaceLimitOrderRequest
            {
                ExchangeId = _exchangeId,
                Symbol = symbol,
                Side = side.ToString(),
                Price = (double)price,
                Quantity = (double)quantity,
                Venue = _venue.ToString(),
                PositionSide = positionSide.ToString(),
                MarginSideEffect = marginSideEffect.ToString(),
                ReduceOnly = reduceOnly
            });
            return MapOrder(response);
        }

        public async Task<ExchangeOrder> PlaceStopLimitOrderAsync(
            string symbol, ExchangeOrderSide side, decimal stopPrice, decimal limitPrice, decimal quantity,
            PositionSide positionSide = PositionSide.Both,
            MarginSideEffect marginSideEffect = MarginSideEffect.None,
            bool reduceOnly = false)
        {
            var response = await _broker.PlaceStopLimitOrderAsync(new PlaceStopLimitOrderRequest
            {
                ExchangeId = _exchangeId,
                Symbol = symbol,
                Side = side.ToString(),
                StopPrice = (double)stopPrice,
                LimitPrice = (double)limitPrice,
                Quantity = (double)quantity,
                Venue = _venue.ToString(),
                PositionSide = positionSide.ToString(),
                MarginSideEffect = marginSideEffect.ToString(),
                ReduceOnly = reduceOnly
            });
            return MapOrder(response);
        }

        public async Task<ExchangeOrder> GetOrderAsync(string symbol, string orderId)
        {
            var response = await _broker.GetOrderAsync(new GetOrderRequest
            {
                ExchangeId = _exchangeId,
                Symbol = symbol,
                OrderId = orderId
            });
            return MapOrder(response);
        }

        public async Task<ExchangeOrder> CancelOrderAsync(string symbol, string orderId)
        {
            var response = await _broker.CancelOrderAsync(new CancelOrderRequest
            {
                ExchangeId = _exchangeId,
                Symbol = symbol,
                OrderId = orderId
            });
            return MapOrder(response);
        }

        public async Task<IEnumerable<ExchangeOrder>> GetOpenOrdersAsync()
        {
            var response = await _broker.GetOpenOrdersAsync(new GetOpenOrdersRequest { ExchangeId = _exchangeId });
            return response.Orders.Select(MapOrder);
        }

        public async Task<IEnumerable<ExchangeCandlestick>> GetCandlesticksAsync(
            string symbol, CandleInterval interval, DateTime from, DateTime to)
        {
            var response = await _marketData.GetCandlesAsync(new GetCandlesRequest
            {
                ExchangeId = _exchangeId,
                Symbol = symbol,
                Interval = IntervalToString(interval),
                From = Timestamp.FromDateTime(DateTime.SpecifyKind(from, DateTimeKind.Utc)),
                To = Timestamp.FromDateTime(DateTime.SpecifyKind(to, DateTimeKind.Utc))
            });

            return response.Candles.Select(c => new ExchangeCandlestick
            {
                ExchangeId = c.ExchangeId,
                Symbol = c.Symbol,
                Interval = interval,
                OpenTime = c.OpenTime.ToDateTime(),
                CloseTime = c.CloseTime.ToDateTime(),
                Open = (decimal)c.Open,
                High = (decimal)c.High,
                Low = (decimal)c.Low,
                Close = (decimal)c.Close,
                Volume = (decimal)c.Volume,
                QuoteVolume = (decimal)c.QuoteVolume,
                NumberOfTrades = c.NumberOfTrades,
                IsClosed = c.IsClosed
            });
        }

        public async Task SubscribeCandlestickAsync(string symbol, CandleInterval interval, Action<ExchangeCandlestick> onCandle)
        {
            _streamCts = new CancellationTokenSource();
            var request = new SubscribeCandlesRequest
            {
                ExchangeId = _exchangeId,
                Symbol = symbol,
                Interval = IntervalToString(interval)
            };

            var stream = _marketData.SubscribeCandles(request, cancellationToken: _streamCts.Token);

            _ = Task.Run(async () =>
            {
                try
                {
                    while (await stream.ResponseStream.MoveNext(_streamCts.Token))
                    {
                        var update = stream.ResponseStream.Current;
                        var candle = new ExchangeCandlestick
                        {
                            ExchangeId = update.Candle.ExchangeId,
                            Symbol = update.Candle.Symbol,
                            Interval = interval,
                            OpenTime = update.Candle.OpenTime.ToDateTime(),
                            CloseTime = update.Candle.CloseTime.ToDateTime(),
                            Open = (decimal)update.Candle.Open,
                            High = (decimal)update.Candle.High,
                            Low = (decimal)update.Candle.Low,
                            Close = (decimal)update.Candle.Close,
                            Volume = (decimal)update.Candle.Volume,
                            QuoteVolume = (decimal)update.Candle.QuoteVolume,
                            NumberOfTrades = update.Candle.NumberOfTrades,
                            IsClosed = update.Candle.IsClosed
                        };
                        onCandle(candle);
                    }
                }
                catch (OperationCanceledException) { }
            });
        }

        public Task UnsubscribeAllAsync()
        {
            _streamCts?.Cancel();
            _streamCts?.Dispose();
            _streamCts = null;
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<ExchangePosition>> GetPositionsAsync()
        {
            var response = await _broker.GetPositionsAsync(new GetPositionsRequest { ExchangeId = _exchangeId });
            return response.Positions.Select(p => new ExchangePosition
            {
                ExchangeId = p.ExchangeId,
                Symbol = p.Symbol,
                Side = Enum.TryParse<PositionSide>(p.Side, true, out var s) ? s : PositionSide.Both,
                Quantity = (decimal)p.Quantity,
                EntryPrice = (decimal)p.EntryPrice,
                MarkPrice = (decimal)p.MarkPrice,
                Leverage = p.Leverage,
                UnrealizedPnl = (decimal)p.UnrealizedPnl,
                Notional = (decimal)p.Notional,
                Timestamp = p.Timestamp.ToDateTime()
            });
        }

        public async Task SetLeverageAsync(string symbol, int leverage)
        {
            await _broker.SetLeverageAsync(new SetLeverageRequest
            {
                ExchangeId = _exchangeId,
                Symbol = symbol,
                Leverage = leverage
            });
        }

        public async Task SubscribeUserStreamAsync(Action<ExchangeFill> onFill)
        {
            var stream = _broker.SubscribeOrderUpdates(new SubscribeOrderUpdatesRequest { ExchangeId = _exchangeId });

            _ = Task.Run(async () =>
            {
                try
                {
                    while (await stream.ResponseStream.MoveNext(CancellationToken.None))
                    {
                        var evt = stream.ResponseStream.Current;
                        var fill = new ExchangeFill
                        {
                            ExchangeId = evt.ExchangeId,
                            OrderId = evt.OrderId,
                            ClientOrderId = evt.ClientOrderId,
                            Symbol = evt.Symbol,
                            Side = Enum.TryParse<ExchangeOrderSide>(evt.Side, true, out var side) ? side : ExchangeOrderSide.Buy,
                            FilledQuantity = (decimal)evt.FilledQuantity,
                            Price = (decimal)evt.Price,
                            Commission = (decimal)evt.Commission,
                            CommissionAsset = evt.CommissionAsset,
                            Status = Enum.TryParse<ExchangeOrderStatus>(evt.Status, true, out var status) ? status : ExchangeOrderStatus.New,
                            Timestamp = evt.Timestamp.ToDateTime()
                        };
                        onFill(fill);
                    }
                }
                catch (OperationCanceledException) { }
            });
        }

        private static ExchangeOrder MapOrder(OrderResponse r)
        {
            return new ExchangeOrder
            {
                ExchangeId = r.ExchangeId,
                OrderId = r.OrderId,
                ClientOrderId = r.ClientOrderId,
                Symbol = r.Symbol,
                Side = Enum.TryParse<ExchangeOrderSide>(r.Side, true, out var side) ? side : ExchangeOrderSide.Buy,
                Type = Enum.TryParse<ExchangeOrderType>(r.Type, true, out var type) ? type : ExchangeOrderType.Market,
                Status = Enum.TryParse<ExchangeOrderStatus>(r.Status, true, out var status) ? status : ExchangeOrderStatus.New,
                Price = (decimal)r.Price,
                StopPrice = (decimal)r.StopPrice,
                Quantity = (decimal)r.Quantity,
                FilledQuantity = (decimal)r.FilledQuantity,
                QuoteQuantity = (decimal)r.QuoteQuantity,
                Timestamp = r.Timestamp.ToDateTime()
            };
        }

        private static string IntervalToString(CandleInterval interval)
        {
            switch (interval)
            {
                case CandleInterval.Minute_1: return "1m";
                case CandleInterval.Minute_3: return "3m";
                case CandleInterval.Minute_5: return "5m";
                case CandleInterval.Minute_15: return "15m";
                case CandleInterval.Minute_30: return "30m";
                case CandleInterval.Hour_1: return "1h";
                case CandleInterval.Hour_2: return "2h";
                case CandleInterval.Hour_4: return "4h";
                case CandleInterval.Hour_6: return "6h";
                case CandleInterval.Hour_8: return "8h";
                case CandleInterval.Hour_12: return "12h";
                case CandleInterval.Day_1: return "1d";
                case CandleInterval.Day_3: return "3d";
                case CandleInterval.Week_1: return "1w";
                case CandleInterval.Month_1: return "1M";
                default: return interval.ToString();
            }
        }
    }
}
