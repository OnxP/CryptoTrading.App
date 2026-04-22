using System;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core
{
    public static class CandleStickIntervalHelper
    {
        public static DateTime NextCandleStickTime(DateTime dateTime, CandleInterval interval)
        {
            return CalculateCandleStickTimeFrom(dateTime, interval, -1);
        }

        public static DateTime PreviousCandleStickTime(DateTime dateTime, CandleInterval interval)
        {
            return CalculateCandleStickTimeFrom(dateTime, interval, 1);
        }

        public static DateTime CalculateCandleStickTimeFrom(DateTime dateTime, CandleInterval interval, int number)
        {
            int candleSticksToLoad = number;
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

        public static int CalculateNumberBetweenDates(DateTime earliestDate, DateTime currentDate, CandleInterval interval, int indicatorMultiplier)
        {
            var size = 1;
            while (currentDate >=
                   CandleStickIntervalHelper.CalculateCandleStickTimeFrom(earliestDate,
                       interval, size * indicatorMultiplier))
            {
                size++;
            }

            return size;
        }
    }
}
