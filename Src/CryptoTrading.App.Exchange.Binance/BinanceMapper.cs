using Binance.Net.Enums;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Exchange.Binance
{
    public static class BinanceMapper
    {
        public static ExchangeOrderSide MapOrderSide(OrderSide side)
        {
            return side == OrderSide.Buy ? ExchangeOrderSide.Buy : ExchangeOrderSide.Sell;
        }

        public static OrderSide MapToBinanceOrderSide(ExchangeOrderSide side)
        {
            return side == ExchangeOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell;
        }

        public static ExchangeOrderStatus MapOrderStatus(OrderStatus status)
        {
            return status switch
            {
                OrderStatus.New => ExchangeOrderStatus.New,
                OrderStatus.PartiallyFilled => ExchangeOrderStatus.PartiallyFilled,
                OrderStatus.Filled => ExchangeOrderStatus.Filled,
                OrderStatus.Canceled => ExchangeOrderStatus.Cancelled,
                OrderStatus.PendingCancel => ExchangeOrderStatus.Cancelled,
                OrderStatus.Rejected => ExchangeOrderStatus.Rejected,
                OrderStatus.Expired => ExchangeOrderStatus.Expired,
                _ => ExchangeOrderStatus.New
            };
        }

        public static ExchangeOrderType MapOrderType(SpotOrderType type)
        {
            return type switch
            {
                SpotOrderType.Market => ExchangeOrderType.Market,
                SpotOrderType.Limit => ExchangeOrderType.Limit,
                SpotOrderType.StopLossLimit => ExchangeOrderType.StopLimit,
                _ => ExchangeOrderType.Market
            };
        }

        public static KlineInterval MapToBinanceCandleInterval(CandleInterval interval)
        {
            return interval switch
            {
                CandleInterval.Minute_1 => KlineInterval.OneMinute,
                CandleInterval.Minute_3 => KlineInterval.ThreeMinutes,
                CandleInterval.Minute_5 => KlineInterval.FiveMinutes,
                CandleInterval.Minute_15 => KlineInterval.FifteenMinutes,
                CandleInterval.Minute_30 => KlineInterval.ThirtyMinutes,
                CandleInterval.Hour_1 => KlineInterval.OneHour,
                CandleInterval.Hour_2 => KlineInterval.TwoHour,
                CandleInterval.Hour_4 => KlineInterval.FourHour,
                CandleInterval.Hour_6 => KlineInterval.SixHour,
                CandleInterval.Hour_8 => KlineInterval.EightHour,
                CandleInterval.Hour_12 => KlineInterval.TwelveHour,
                CandleInterval.Day_1 => KlineInterval.OneDay,
                CandleInterval.Day_3 => KlineInterval.ThreeDay,
                CandleInterval.Week_1 => KlineInterval.OneWeek,
                CandleInterval.Month_1 => KlineInterval.OneMonth,
                _ => KlineInterval.OneMinute
            };
        }

        public static CandleInterval MapCandleInterval(KlineInterval interval)
        {
            return interval switch
            {
                KlineInterval.OneMinute => CandleInterval.Minute_1,
                KlineInterval.ThreeMinutes => CandleInterval.Minute_3,
                KlineInterval.FiveMinutes => CandleInterval.Minute_5,
                KlineInterval.FifteenMinutes => CandleInterval.Minute_15,
                KlineInterval.ThirtyMinutes => CandleInterval.Minute_30,
                KlineInterval.OneHour => CandleInterval.Hour_1,
                KlineInterval.TwoHour => CandleInterval.Hour_2,
                KlineInterval.FourHour => CandleInterval.Hour_4,
                KlineInterval.SixHour => CandleInterval.Hour_6,
                KlineInterval.EightHour => CandleInterval.Hour_8,
                KlineInterval.TwelveHour => CandleInterval.Hour_12,
                KlineInterval.OneDay => CandleInterval.Day_1,
                KlineInterval.ThreeDay => CandleInterval.Day_3,
                KlineInterval.OneWeek => CandleInterval.Week_1,
                KlineInterval.OneMonth => CandleInterval.Month_1,
                _ => CandleInterval.Minute_1
            };
        }
    }
}
