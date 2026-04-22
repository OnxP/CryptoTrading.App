using System;
using Binance;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core
{
    public static class CandleStickIntervalHelper
    {
        public static DateTime NextCandleStickTime(DateTime dateTime, CandlestickInterval interval)
        {
            return CalculateCandleStickTimeFrom(dateTime, interval, -1);
        }

        public static DateTime PreviousCandleStickTime(DateTime dateTime, CandlestickInterval interval)
        {
            return CalculateCandleStickTimeFrom(dateTime, interval, 1);
        }

        // Neutral overloads: CandleInterval shares ordinals 0-14 with CandlestickInterval,
        // so the cast is safe. Bundled overloads stay alive during PR 5c; PR 5d retypes
        // the helper to neutral-only and deletes the bundled overloads.
        public static DateTime NextCandleStickTime(DateTime dateTime, CandleInterval interval)
        {
            return CalculateCandleStickTimeFrom(dateTime, (CandlestickInterval)(int)interval, -1);
        }

        public static DateTime PreviousCandleStickTime(DateTime dateTime, CandleInterval interval)
        {
            return CalculateCandleStickTimeFrom(dateTime, (CandlestickInterval)(int)interval, 1);
        }

        public static DateTime CalculateCandleStickTimeFrom(DateTime dateTime, CandleInterval interval, int number)
        {
            return CalculateCandleStickTimeFrom(dateTime, (CandlestickInterval)(int)interval, number);
        }

        public static int CalculateNumberBetweenDates(DateTime earliestDate, DateTime currentDate, CandleInterval interval, int indicatorMultiplier)
        {
            return CalculateNumberBetweenDates(earliestDate, currentDate, (CandlestickInterval)(int)interval, indicatorMultiplier);
        }

        public static DateTime CalculateCandleStickTimeFrom(DateTime dateTime, CandlestickInterval interval, int number)
        {
            int candleSticksToLoad = number;
            return interval switch
            {
                CandlestickInterval.Minute => dateTime.AddMinutes(-1 * candleSticksToLoad),
                CandlestickInterval.Minutes_3 => dateTime.AddMinutes(-3 * candleSticksToLoad),
                CandlestickInterval.Minutes_5 => dateTime.AddMinutes(-5 * candleSticksToLoad),
                CandlestickInterval.Minutes_15 => dateTime.AddMinutes(-15 * candleSticksToLoad),
                CandlestickInterval.Minutes_30 => dateTime.AddMinutes(-30 * candleSticksToLoad),
                CandlestickInterval.Hour => dateTime.AddHours(-1 * candleSticksToLoad),
                CandlestickInterval.Hours_2 => dateTime.AddHours(-2 * candleSticksToLoad),
                CandlestickInterval.Hours_4 => dateTime.AddHours(-4 * candleSticksToLoad),
                CandlestickInterval.Hours_6 => dateTime.AddHours(-6 * candleSticksToLoad),
                CandlestickInterval.Hours_8 => dateTime.AddHours(-8 * candleSticksToLoad),
                CandlestickInterval.Hours_12 => dateTime.AddHours(-12 * candleSticksToLoad),
                CandlestickInterval.Day => dateTime.AddDays(-1 * candleSticksToLoad),
                CandlestickInterval.Days_3 => dateTime.AddDays(-3 * candleSticksToLoad),
                CandlestickInterval.Week => dateTime.AddDays(-7 * candleSticksToLoad),
                CandlestickInterval.Month => dateTime.AddMonths(-1 * candleSticksToLoad),
                _ => dateTime,
            };
        }

        public static int CalculateNumberBetweenDates(DateTime earliestDate, DateTime currentDate, CandlestickInterval interval, int indicatorMultiplier)
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
