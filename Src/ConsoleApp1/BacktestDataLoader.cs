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
        // PR 5h: returns neutral ExchangeCandlestick directly. Callers that
        // were threading bundled Candlestick through the backtest now consume
        // the neutral type end-to-end.
        public static List<ExchangeCandlestick> Load(
            string symbol,
            CandleInterval interval,
            DateTime from,
            DateTime to)
        {
            using var ctx = new CryptoDbContext();
            ctx.Configuration.AutoDetectChangesEnabled = false;
            ctx.Configuration.LazyLoadingEnabled = false;
            ctx.Database.CommandTimeout = 600;

            var rows = ctx.CandleSticks
                .AsNoTracking()
                .Where(c => c.Symbol == symbol
                            && c.Interval == interval
                            && c.OpenTime >= from
                            && c.OpenTime < to)
                .OrderBy(c => c.OpenTime)
                .ToList();

            return rows.Select(CandleStickDb.ConvertObject).ToList();
        }
    }
}
