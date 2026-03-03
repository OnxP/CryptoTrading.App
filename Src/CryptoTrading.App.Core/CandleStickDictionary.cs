using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Binance;
using Skender.Stock.Indicators;

namespace CryptoTrading.App.Core
{
    public class CandleStickDictionary : ConcurrentDictionary<DateTime,Candlestick>
    {
        public int NumberOfCandleSticksToKeep { get; }

        public CandleStickDictionary(int numberOfCandleSticksToKeep)
        {
            NumberOfCandleSticksToKeep = numberOfCandleSticksToKeep;
        }
        public Candlestick Current => this[this.Where(x=>x.Value!=null).Max(x => x.Key)];
        public CandlestickInterval Interval => Values.First(x => x != null).Interval;
        public bool Ready => Count >= NumberOfCandleSticksToKeep;
        public bool HasMissing => Values.Any(x => x == null);

        public IEnumerable<IQuote> CandleSticks => Values.Where(x=>x!=null).Select(x=>new Quote
        {
            Timestamp = x.OpenTime,
            Open = x.Open,
            High = x.High,
            Low = x.Low,
            Close = x.Close,
            Volume = x.Volume
        }).OrderByDescending(X=>X.Timestamp);

        public IEnumerable<Candlestick> GetCandlesticks => Values.OrderByDescending(X=>X.CloseTime);

        public void Add(Candlestick item)
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
                if (Count > NumberOfCandleSticksToKeep) TryRemove(Keys.Min(),out item);
            }

        }

        public void AddRange(IEnumerable<Candlestick> candlesticks)
        {
            foreach (var candlestick in candlesticks)
            {
                Add(candlestick);
            }
        }

        public IOrderedEnumerable<Candlestick> GroupCandleSticks(int indicatorMultiplier)
        {
            if (indicatorMultiplier == 1) return Values.OrderBy(x => x.OpenTime);

            var startDate = Keys.Where(x=>x.Minute==0).Min();
            var orderKeys = Keys.OrderByDescending(x=>x).Where(x => x >= startDate);
            var keyGrouping = orderKeys.Where(x=>x>=startDate).Select((x, idx) => new { x, idx })
                .GroupBy(x => x.idx / indicatorMultiplier)
                .Select(g => g.OrderByDescending(b => b.x).Select(a => a.x));

            var list = new List<Candlestick>();
            foreach (var group in keyGrouping)
            {
                var candlestickList = group.Select(x => this[x]);
                list.Add(Aggregate(candlestickList));
            }

            return list.OrderBy(x=>x.OpenTime);
        }

        private Candlestick Aggregate(IEnumerable<Candlestick> list)
        {
            var candlesticks = list.OrderByDescending(x=>x.OpenTime).ToList();
            return new Candlestick(
                candlesticks.First().Symbol,
                candlesticks.First().Interval,
                candlesticks.Min(x => x.OpenTime),
                candlesticks.Last().Open,
                candlesticks.Max(x => x.High),
                candlesticks.Min(x => x.Low),
                candlesticks.First().Close,
                candlesticks.Sum(x => x.Volume),
                candlesticks.Max(x=>x.CloseTime),
                candlesticks.Sum(x => x.QuoteAssetVolume),
                candlesticks.Sum(x => x.NumberOfTrades),
                candlesticks.Sum(x => x.TakerBuyBaseAssetVolume),
                candlesticks.Sum(x => x.TakerBuyQuoteAssetVolume));
        }

    }
}
