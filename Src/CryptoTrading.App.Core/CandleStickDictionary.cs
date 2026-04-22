using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using CryptoTrading.App.Core.Exchange;
using Skender.Stock.Indicators;

namespace CryptoTrading.App.Core
{
    public class CandleStickDictionary : ConcurrentDictionary<DateTime, ExchangeCandlestick>
    {
        public int NumberOfCandleSticksToKeep { get; }

        public CandleStickDictionary(int numberOfCandleSticksToKeep)
        {
            NumberOfCandleSticksToKeep = numberOfCandleSticksToKeep;
        }
        public ExchangeCandlestick Current => this[this.Where(x => x.Value != null).Max(x => x.Key)];
        public CandleInterval Interval => Values.First(x => x != null).Interval;
        public bool Ready => Count >= NumberOfCandleSticksToKeep;
        public bool HasMissing => Values.Any(x => x == null);

        public IEnumerable<IQuote> CandleSticks => Values.Where(x => x != null).Select(x => new Quote
        {
            Timestamp = x.OpenTime,
            Open = x.Open,
            High = x.High,
            Low = x.Low,
            Close = x.Close,
            Volume = x.Volume
        }).OrderByDescending(X => X.Timestamp);

        public IEnumerable<ExchangeCandlestick> GetCandlesticks => Values.OrderByDescending(X => X.CloseTime);

        public void Add(ExchangeCandlestick item)
        {
            if (Count == 0)
            {
                TryAdd(item.OpenTime, item);
                return;
            }

            if (!ContainsKey(item.OpenTime))
            {
                var nextTime = CandleStickIntervalHelper.NextCandleStickTime(Current.OpenTime, Interval);
                while (nextTime < item.OpenTime)
                {
                    TryAdd(nextTime, null);
                    nextTime = CandleStickIntervalHelper.NextCandleStickTime(nextTime, Interval);
                }

                TryAdd(item.OpenTime, item);
                if (Count > NumberOfCandleSticksToKeep) TryRemove(Keys.Min(), out item);
            }

        }

        public void AddRange(IEnumerable<ExchangeCandlestick> candlesticks)
        {
            foreach (var candlestick in candlesticks)
            {
                Add(candlestick);
            }
        }

        public IOrderedEnumerable<ExchangeCandlestick> GroupCandleSticks(int indicatorMultiplier)
        {
            if (indicatorMultiplier == 1) return Values.OrderBy(x => x.OpenTime);

            var startDate = Keys.Where(x => x.Minute == 0).Min();
            var orderKeys = Keys.OrderByDescending(x => x).Where(x => x >= startDate);
            var keyGrouping = orderKeys.Where(x => x >= startDate).Select((x, idx) => new { x, idx })
                .GroupBy(x => x.idx / indicatorMultiplier)
                .Select(g => g.OrderByDescending(b => b.x).Select(a => a.x));

            var list = new List<ExchangeCandlestick>();
            foreach (var group in keyGrouping)
            {
                var candlestickList = group.Select(x => this[x]);
                list.Add(Aggregate(candlestickList));
            }

            return list.OrderBy(x => x.OpenTime);
        }

        private ExchangeCandlestick Aggregate(IEnumerable<ExchangeCandlestick> list)
        {
            var candlesticks = list.OrderByDescending(x => x.OpenTime).ToList();
            return new ExchangeCandlestick
            {
                Symbol = candlesticks.First().Symbol,
                Interval = candlesticks.First().Interval,
                OpenTime = candlesticks.Min(x => x.OpenTime),
                Open = candlesticks.Last().Open,
                High = candlesticks.Max(x => x.High),
                Low = candlesticks.Min(x => x.Low),
                Close = candlesticks.First().Close,
                Volume = candlesticks.Sum(x => x.Volume),
                CloseTime = candlesticks.Max(x => x.CloseTime),
                QuoteVolume = candlesticks.Sum(x => x.QuoteVolume),
                NumberOfTrades = candlesticks.Sum(x => x.NumberOfTrades),
                IsClosed = true,
            };
        }

    }
}
