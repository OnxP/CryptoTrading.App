using Binance;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Exchange;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;

namespace CryptoTrading.App.BackTesting
{
    internal static class BacktestDataLoader
    {
        public static List<Candlestick> Load(
            string symbol,
            CandlestickInterval interval,
            DateTime from,
            DateTime to)
        {
            using var ctx = new CryptoDbContext();
            ctx.Configuration.AutoDetectChangesEnabled = false;
            ctx.Configuration.LazyLoadingEnabled = false;
            ctx.Database.CommandTimeout = 600;

            // PR 5f: CandleStickDb.Interval is neutral CandleInterval; bridge here.
            var neutralInterval = (CandleInterval)(int)interval;
            var rows = ctx.CandleSticks
                .AsNoTracking()
                .Where(c => c.Symbol == symbol
                            && c.Interval == neutralInterval
                            && c.OpenTime >= from
                            && c.OpenTime < to)
                .OrderBy(c => c.OpenTime)
                .ToList();

            return rows.Select(r => new Candlestick(
                symbol: r.Symbol,
                interval: (CandlestickInterval)(int)r.Interval,
                openTime: r.OpenTime,
                open: (decimal)r.Open,
                high: (decimal)r.High,
                low: (decimal)r.Low,
                close: (decimal)r.Close,
                volume: r.Volume < 0 ? 0m : r.Volume,
                closeTime: r.CloseTime,
                quoteAssetVolume: r.QuoteAssetVolume,
                numberOfTrades: r.NumberOfTrades,
                takerBuyBaseAssetVolume: r.TakerBuyBaseAssetVolume,
                takerBuyQuoteAssetVolume: r.TakerBuyQuoteAssetVolume)).ToList();
        }
    }
}
