using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Persistence;
using CryptoTrading.App.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.MarketDataService
{
    public class CandleGapFiller
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ExchangeProviderRegistry _registry;
        private readonly ILogger<CandleGapFiller> _logger;

        public CandleGapFiller(
            IServiceScopeFactory scopeFactory,
            ExchangeProviderRegistry registry,
            ILogger<CandleGapFiller> logger)
        {
            _scopeFactory = scopeFactory;
            _registry = registry;
            _logger = logger;
        }

        public async Task FillGapsAsync(
            string exchangeId,
            string symbol,
            CandleInterval interval,
            int cacheDays,
            CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CryptoDbContextPg>();
            var provider = _registry.Get(exchangeId);
            var intervalInt = (int)interval;

            var latestCandle = await db.Candlesticks
                .Where(c => c.ExchangeId == exchangeId && c.Symbol == symbol && c.Interval == intervalInt)
                .OrderByDescending(c => c.OpenTime)
                .FirstOrDefaultAsync(ct);

            var from = latestCandle?.CloseTime ?? DateTime.UtcNow.AddDays(-cacheDays);
            var to = DateTime.UtcNow;

            var gap = to - from;
            if (gap < CandleIntervalHelper.ToTimeSpan(interval))
            {
                _logger.LogDebug("No gap for {Exchange}/{Symbol}/{Interval}", exchangeId, symbol, interval);
                return;
            }

            _logger.LogInformation("Gap filling {Exchange}/{Symbol}/{Interval}: {From} -> {To} ({Gap})",
                exchangeId, symbol, CandleIntervalHelper.ToShortString(interval), from, to, gap);

            var candles = await provider.GetCandlesticksAsync(symbol, interval, from, to);
            var candleList = candles.ToList();

            if (candleList.Count == 0)
                return;

            var inserted = 0;
            foreach (var batch in candleList.Where(c => c.IsClosed).Chunk(500))
            {
                foreach (var candle in batch)
                {
                    var entity = CandlestickEntity.FromExchangeCandlestick(candle);
                    entity.ExchangeId = exchangeId;

                    var exists = await db.Candlesticks.AnyAsync(c =>
                        c.ExchangeId == exchangeId &&
                        c.Symbol == symbol &&
                        c.Interval == intervalInt &&
                        c.OpenTime == entity.OpenTime, ct);

                    if (!exists)
                    {
                        db.Candlesticks.Add(entity);
                        inserted++;
                    }
                }
                await db.SaveChangesAsync(ct);
            }

            _logger.LogInformation("Gap filled {Exchange}/{Symbol}/{Interval}: {Count} candles inserted",
                exchangeId, symbol, CandleIntervalHelper.ToShortString(interval), inserted);
        }
    }
}
