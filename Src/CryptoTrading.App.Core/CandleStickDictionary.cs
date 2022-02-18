using System;
using System.Collections.Generic;
using System.Data.Entity.Core.Metadata.Edm;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using Binance;

namespace CryptoTrading.App.Core
{
    public class CandleStickDictionary : Dictionary<DateTime,Candlestick>
    {
        public int NumberOfCandleSticksToKeep { get; }

        public CandleStickDictionary(int numberOfCandleSticksToKeep)
        {
            NumberOfCandleSticksToKeep = numberOfCandleSticksToKeep+1;
        }
        public Candlestick Current => this[this.Max(x => x.Key)];
        public CandlestickInterval Interval => this.Values.First().Interval;
        public bool Ready => this.Count >= NumberOfCandleSticksToKeep;
        public bool HasMissing => this.Values.Any(x => x == null);

        public void Add(Candlestick item)
        {
            if (Count == 0)
            {
                this.Add(item.OpenTime, item);
                return;
            }

            if (!ContainsKey(item.OpenTime))
            {
                var nextTime = CandleStickIntervalHelper.NextCandleStickTime(Current.OpenTime, Interval);
                while (nextTime < item.OpenTime)
                {
                    this.Add(nextTime,null);
                    nextTime = CandleStickIntervalHelper.NextCandleStickTime(nextTime, Interval);
                }
                this.Add(item.OpenTime, item);
                if (Count > NumberOfCandleSticksToKeep) Remove(Keys.Min());
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
            if (indicatorMultiplier == 1) return Values.OrderByDescending(x => x.OpenTime);

            var startDate = Keys.Where(x=>x.Minute==0).Min();

            var keyGrouping = Keys.Where(x=>x>=startDate).Select((x, idx) => new { x, idx })
                .GroupBy(x => x.idx / indicatorMultiplier)
                .Select(g => g.Select(a => a.x));

            var list = new List<Candlestick>();
            foreach (var group in keyGrouping)
            {
                var candlestickList = group.Select(x => this[x]);
                list.Add(Aggregate(candlestickList));
            }

            return list.OrderByDescending(x=>x.OpenTime);
        }

        private Candlestick Aggregate(IEnumerable<Candlestick> list)
        {
            var candlesticks = list.OrderByDescending(x=>x.OpenTime).ToList();
            return new Candlestick(
                candlesticks.First().Symbol,
                candlesticks.First().Interval,
                candlesticks.Min(x => x.OpenTime),
                candlesticks.First().Open,
                candlesticks.Max(x => x.High),
                candlesticks.Min(x => x.Low),
                candlesticks.Last().Close,
                candlesticks.Sum(x => x.Volume),
                candlesticks.Last().CloseTime,
                candlesticks.Sum(x => x.QuoteAssetVolume),
                candlesticks.Sum(x => x.NumberOfTrades),
                candlesticks.Sum(x => x.TakerBuyBaseAssetVolume),
                candlesticks.Sum(x => x.TakerBuyQuoteAssetVolume));
        }

    }
}
