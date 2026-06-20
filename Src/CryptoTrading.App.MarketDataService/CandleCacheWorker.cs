using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Persistence;
using CryptoTrading.App.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.MarketDataService
{
    public class CandleCacheWorker : BackgroundService
    {
        private readonly ExchangeProviderRegistry _registry;
        private readonly CandleGapFiller _gapFiller;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CandleCacheWorker> _logger;

        private readonly ConcurrentDictionary<string, Channel<CandleUpdate>> _subscribers = new();
        private int _subscriberCounter;

        public CandleCacheWorker(
            ExchangeProviderRegistry registry,
            CandleGapFiller gapFiller,
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<CandleCacheWorker> logger)
        {
            _registry = registry;
            _gapFiller = gapFiller;
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public int ActiveSubscriptionCount => _subscribers.Count;

        public ChannelReader<CandleUpdate> Subscribe(string exchangeId, string symbol, string interval)
        {
            var key = $"{Interlocked.Increment(ref _subscriberCounter)}_{exchangeId}_{symbol}_{interval}";
            var channel = Channel.CreateBounded<CandleUpdate>(new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });
            _subscribers[key] = channel;
            return channel.Reader;
        }

        public void Unsubscribe(ChannelReader<CandleUpdate> reader)
        {
            var toRemove = _subscribers.FirstOrDefault(kvp => kvp.Value.Reader == reader);
            if (toRemove.Key != null)
            {
                _subscribers.TryRemove(toRemove.Key, out _);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var cacheDays = _configuration.GetValue("MarketData:CacheDays", 90);
            var intervalsCsv = _configuration.GetValue("MarketData:CacheIntervals", "15m,1h,4h");
            var intervals = intervalsCsv.Split(',')
                .Select(s => CandleIntervalHelper.Parse(s.Trim()))
                .ToList();

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CryptoDbContextPg>();

            var tradingPairs = await db.TradingPairs
                .Where(tp => tp.IsActive)
                .ToListAsync(stoppingToken);

            if (tradingPairs.Count == 0)
            {
                _logger.LogWarning("No active trading pairs configured. Candle cache worker idle.");
                await Task.Delay(Timeout.Infinite, stoppingToken);
                return;
            }

            var gapFillTasks = new List<Task>();
            foreach (var pair in tradingPairs)
            {
                foreach (var interval in intervals)
                {
                    gapFillTasks.Add(_gapFiller.FillGapsAsync(pair.ExchangeId, pair.Symbol, interval, cacheDays, stoppingToken));
                }
            }
            await Task.WhenAll(gapFillTasks);
            _logger.LogInformation("Gap fill complete for {Count} pairs x {Intervals} intervals", tradingPairs.Count, intervals.Count);

            var subscriptionTasks = new List<Task>();
            foreach (var pair in tradingPairs)
            {
                foreach (var interval in intervals)
                {
                    subscriptionTasks.Add(SubscribeToCandles(pair.ExchangeId, pair.Symbol, interval, stoppingToken));
                }
            }

            _logger.LogInformation("Subscribed to {Count} candle streams", subscriptionTasks.Count);
            await Task.WhenAll(subscriptionTasks);
        }

        private async Task SubscribeToCandles(string exchangeId, string symbol, CandleInterval interval, CancellationToken ct)
        {
            var provider = _registry.Get(exchangeId);
            var intervalStr = CandleIntervalHelper.ToShortString(interval);

            try
            {
                await provider.SubscribeCandlestickAsync(symbol, interval, candle =>
                {
                    if (candle.IsClosed)
                    {
                        _ = PersistCandleAsync(exchangeId, candle);
                    }

                    var update = new CandleUpdate
                    {
                        ExchangeId = exchangeId,
                        Candle = candle,
                        EventTime = DateTime.UtcNow
                    };

                    foreach (var kvp in _subscribers)
                    {
                        if (kvp.Key.Contains($"_{exchangeId}_{symbol}_{intervalStr}"))
                        {
                            kvp.Value.Writer.TryWrite(update);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket subscription failed for {Exchange}/{Symbol}/{Interval}",
                    exchangeId, symbol, intervalStr);
            }

            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
        }

        private async Task PersistCandleAsync(string exchangeId, ExchangeCandlestick candle)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CryptoDbContextPg>();

                var intervalInt = (int)candle.Interval;
                var exists = await db.Candlesticks.AnyAsync(c =>
                    c.ExchangeId == exchangeId &&
                    c.Symbol == candle.Symbol &&
                    c.Interval == intervalInt &&
                    c.OpenTime == candle.OpenTime);

                if (!exists)
                {
                    var entity = CandlestickEntity.FromExchangeCandlestick(candle);
                    entity.ExchangeId = exchangeId;
                    db.Candlesticks.Add(entity);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist candle {Symbol}/{Interval}", candle.Symbol, candle.Interval);
            }
        }
    }

    public class CandleUpdate
    {
        public string ExchangeId { get; set; }
        public ExchangeCandlestick Candle { get; set; }
        public DateTime EventTime { get; set; }
    }
}
