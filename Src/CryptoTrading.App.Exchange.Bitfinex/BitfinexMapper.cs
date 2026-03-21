using System;
using Bitfinex.Net.Enums;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Exchange.Bitfinex
{
    public static class BitfinexMapper
    {
        public static ExchangeOrderSide MapOrderSide(OrderSide side)
        {
            return side == OrderSide.Buy ? ExchangeOrderSide.Buy : ExchangeOrderSide.Sell;
        }

        public static OrderSide MapToBitfinexOrderSide(ExchangeOrderSide side)
        {
            return side == ExchangeOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell;
        }

        public static ExchangeOrderStatus MapOrderStatus(OrderStatus status)
        {
            return status switch
            {
                OrderStatus.Active => ExchangeOrderStatus.New,
                OrderStatus.PartiallyFilled => ExchangeOrderStatus.PartiallyFilled,
                OrderStatus.Executed => ExchangeOrderStatus.Filled,
                OrderStatus.Canceled => ExchangeOrderStatus.Cancelled,
                _ => ExchangeOrderStatus.New
            };
        }

        public static KlineInterval MapToBitfinexCandleInterval(CandleInterval interval)
        {
            return interval switch
            {
                CandleInterval.Minute_1 => KlineInterval.OneMinute,
                CandleInterval.Minute_5 => KlineInterval.FiveMinutes,
                CandleInterval.Minute_15 => KlineInterval.FifteenMinutes,
                CandleInterval.Minute_30 => KlineInterval.ThirtyMinutes,
                CandleInterval.Hour_1 => KlineInterval.OneHour,
                CandleInterval.Hour_6 => KlineInterval.SixHours,
                CandleInterval.Hour_12 => KlineInterval.TwelveHours,
                CandleInterval.Day_1 => KlineInterval.OneDay,
                CandleInterval.Week_1 => KlineInterval.SevenDays,
                CandleInterval.Month_1 => KlineInterval.OneMonth,
                _ => KlineInterval.OneMinute
            };
        }

        public static DateTime ComputeCloseTime(DateTime openTime, CandleInterval interval)
        {
            return interval switch
            {
                CandleInterval.Minute_1 => openTime.AddMinutes(1),
                CandleInterval.Minute_3 => openTime.AddMinutes(3),
                CandleInterval.Minute_5 => openTime.AddMinutes(5),
                CandleInterval.Minute_15 => openTime.AddMinutes(15),
                CandleInterval.Minute_30 => openTime.AddMinutes(30),
                CandleInterval.Hour_1 => openTime.AddHours(1),
                CandleInterval.Hour_2 => openTime.AddHours(2),
                CandleInterval.Hour_4 => openTime.AddHours(4),
                CandleInterval.Hour_6 => openTime.AddHours(6),
                CandleInterval.Hour_8 => openTime.AddHours(8),
                CandleInterval.Hour_12 => openTime.AddHours(12),
                CandleInterval.Day_1 => openTime.AddDays(1),
                CandleInterval.Day_3 => openTime.AddDays(3),
                CandleInterval.Week_1 => openTime.AddDays(7),
                CandleInterval.Month_1 => openTime.AddMonths(1),
                _ => openTime
            };
        }

        public static string ToStandardSymbol(string bitfinexSymbol)
        {
            if (bitfinexSymbol.StartsWith("t"))
                bitfinexSymbol = bitfinexSymbol.Substring(1);
            return bitfinexSymbol.Replace("UST", "USDT");
        }

        public static string ToBitfinexSymbol(string standardSymbol)
        {
            return "t" + standardSymbol.Replace("USDT", "UST");
        }
    }
}
