using CryptoTrading.App.Core.Exchange;
using System;

namespace CryptoTrading.App.MarketData
{
    internal static class DbMarketDataHelpers
    {
        // PR 5h: flipped to neutral CandleInterval. Behaviour is unchanged
        // because the bundled CandlestickInterval ordinals were 1:1 with the
        // neutral CandleInterval values, so any caller that previously passed
        // a bundled enum cast to int gets the same arm of the switch.
        public static DateTime CalculateFrom(DateTime dateTime, CandleInterval interval, int NoOfCandleSticks)
        {
            int candleSticksToLoad = -1 * NoOfCandleSticks;
            return interval switch
            {
                CandleInterval.Minute_1 => dateTime.AddMinutes(-1 * candleSticksToLoad),
                CandleInterval.Minute_3 => dateTime.AddMinutes(-3 * candleSticksToLoad),
                CandleInterval.Minute_5 => dateTime.AddMinutes(-5 * candleSticksToLoad),
                CandleInterval.Minute_15 => dateTime.AddMinutes(-15 * candleSticksToLoad),
                CandleInterval.Minute_30 => dateTime.AddMinutes(-30 * candleSticksToLoad),
                CandleInterval.Hour_1 => dateTime.AddHours(-1 * candleSticksToLoad),
                CandleInterval.Hour_2 => dateTime.AddHours(-2 * candleSticksToLoad),
                CandleInterval.Hour_4 => dateTime.AddHours(-4 * candleSticksToLoad),
                CandleInterval.Hour_6 => dateTime.AddHours(-6 * candleSticksToLoad),
                CandleInterval.Hour_8 => dateTime.AddHours(-8 * candleSticksToLoad),
                CandleInterval.Hour_12 => dateTime.AddHours(-12 * candleSticksToLoad),
                CandleInterval.Day_1 => dateTime.AddDays(-1 * candleSticksToLoad),
                CandleInterval.Day_3 => dateTime.AddDays(-3 * candleSticksToLoad),
                CandleInterval.Week_1 => dateTime.AddDays(-7 * candleSticksToLoad),
                CandleInterval.Month_1 => dateTime.AddMonths(-1 * candleSticksToLoad),
                _ => dateTime,
            };
        }
    }
}
