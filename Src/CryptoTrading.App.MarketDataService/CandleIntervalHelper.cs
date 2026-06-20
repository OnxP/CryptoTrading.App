using System;
using System.Collections.Generic;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.MarketDataService
{
    public static class CandleIntervalHelper
    {
        private static readonly Dictionary<string, CandleInterval> StringToInterval = new(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = CandleInterval.Minute_1,
            ["3m"] = CandleInterval.Minute_3,
            ["5m"] = CandleInterval.Minute_5,
            ["15m"] = CandleInterval.Minute_15,
            ["30m"] = CandleInterval.Minute_30,
            ["1h"] = CandleInterval.Hour_1,
            ["2h"] = CandleInterval.Hour_2,
            ["4h"] = CandleInterval.Hour_4,
            ["6h"] = CandleInterval.Hour_6,
            ["8h"] = CandleInterval.Hour_8,
            ["12h"] = CandleInterval.Hour_12,
            ["1d"] = CandleInterval.Day_1,
            ["3d"] = CandleInterval.Day_3,
            ["1w"] = CandleInterval.Week_1,
            ["1M"] = CandleInterval.Month_1,
        };

        private static readonly Dictionary<CandleInterval, string> IntervalToString = new();

        static CandleIntervalHelper()
        {
            foreach (var kvp in StringToInterval)
                IntervalToString[kvp.Value] = kvp.Key;
        }

        public static CandleInterval Parse(string interval)
        {
            if (StringToInterval.TryGetValue(interval, out var result))
                return result;
            throw new ArgumentException($"Unknown candle interval: '{interval}'");
        }

        public static string ToShortString(CandleInterval interval)
        {
            if (IntervalToString.TryGetValue(interval, out var result))
                return result;
            return interval.ToString();
        }

        public static TimeSpan ToTimeSpan(CandleInterval interval)
        {
            switch (interval)
            {
                case CandleInterval.Minute_1: return TimeSpan.FromMinutes(1);
                case CandleInterval.Minute_3: return TimeSpan.FromMinutes(3);
                case CandleInterval.Minute_5: return TimeSpan.FromMinutes(5);
                case CandleInterval.Minute_15: return TimeSpan.FromMinutes(15);
                case CandleInterval.Minute_30: return TimeSpan.FromMinutes(30);
                case CandleInterval.Hour_1: return TimeSpan.FromHours(1);
                case CandleInterval.Hour_2: return TimeSpan.FromHours(2);
                case CandleInterval.Hour_4: return TimeSpan.FromHours(4);
                case CandleInterval.Hour_6: return TimeSpan.FromHours(6);
                case CandleInterval.Hour_8: return TimeSpan.FromHours(8);
                case CandleInterval.Hour_12: return TimeSpan.FromHours(12);
                case CandleInterval.Day_1: return TimeSpan.FromDays(1);
                case CandleInterval.Day_3: return TimeSpan.FromDays(3);
                case CandleInterval.Week_1: return TimeSpan.FromDays(7);
                case CandleInterval.Month_1: return TimeSpan.FromDays(30);
                default: throw new ArgumentOutOfRangeException(nameof(interval));
            }
        }
    }
}
