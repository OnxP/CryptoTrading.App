using System;
using Binance.Net.Enums;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Exchange.Binance;
using FluentAssertions;
using Xunit;

namespace CryptoTrading.App.Tests.Exchange.Providers
{
    public class BinanceMapperTests
    {
        [Theory]
        [InlineData(OrderSide.Buy, ExchangeOrderSide.Buy)]
        [InlineData(OrderSide.Sell, ExchangeOrderSide.Sell)]
        public void MapOrderSide_AllValues_Covered(OrderSide input, ExchangeOrderSide expected)
        {
            BinanceMapper.MapOrderSide(input).Should().Be(expected);
        }

        [Theory]
        [InlineData(ExchangeOrderSide.Buy, OrderSide.Buy)]
        [InlineData(ExchangeOrderSide.Sell, OrderSide.Sell)]
        public void MapToBinanceOrderSide_RoundTrips(ExchangeOrderSide input, OrderSide expected)
        {
            BinanceMapper.MapToBinanceOrderSide(input).Should().Be(expected);
        }

        [Theory]
        [InlineData(SpotOrderType.Market, ExchangeOrderType.Market)]
        [InlineData(SpotOrderType.Limit, ExchangeOrderType.Limit)]
        [InlineData(SpotOrderType.StopLossLimit, ExchangeOrderType.StopLimit)]
        public void MapOrderType_AllValues_Covered(SpotOrderType input, ExchangeOrderType expected)
        {
            BinanceMapper.MapOrderType(input).Should().Be(expected);
        }

        [Theory]
        [InlineData(OrderStatus.New, ExchangeOrderStatus.New)]
        [InlineData(OrderStatus.PartiallyFilled, ExchangeOrderStatus.PartiallyFilled)]
        [InlineData(OrderStatus.Filled, ExchangeOrderStatus.Filled)]
        [InlineData(OrderStatus.Canceled, ExchangeOrderStatus.Cancelled)]
        [InlineData(OrderStatus.PendingCancel, ExchangeOrderStatus.Cancelled)]
        [InlineData(OrderStatus.Rejected, ExchangeOrderStatus.Rejected)]
        [InlineData(OrderStatus.Expired, ExchangeOrderStatus.Expired)]
        public void MapOrderStatus_AllValues_Covered(OrderStatus input, ExchangeOrderStatus expected)
        {
            BinanceMapper.MapOrderStatus(input).Should().Be(expected);
        }

        [Theory]
        [InlineData(KlineInterval.OneMinute, CandleInterval.Minute_1)]
        [InlineData(KlineInterval.ThreeMinutes, CandleInterval.Minute_3)]
        [InlineData(KlineInterval.FiveMinutes, CandleInterval.Minute_5)]
        [InlineData(KlineInterval.FifteenMinutes, CandleInterval.Minute_15)]
        [InlineData(KlineInterval.ThirtyMinutes, CandleInterval.Minute_30)]
        [InlineData(KlineInterval.OneHour, CandleInterval.Hour_1)]
        [InlineData(KlineInterval.TwoHour, CandleInterval.Hour_2)]
        [InlineData(KlineInterval.FourHour, CandleInterval.Hour_4)]
        [InlineData(KlineInterval.SixHour, CandleInterval.Hour_6)]
        [InlineData(KlineInterval.EightHour, CandleInterval.Hour_8)]
        [InlineData(KlineInterval.TwelveHour, CandleInterval.Hour_12)]
        [InlineData(KlineInterval.OneDay, CandleInterval.Day_1)]
        [InlineData(KlineInterval.ThreeDay, CandleInterval.Day_3)]
        [InlineData(KlineInterval.OneWeek, CandleInterval.Week_1)]
        [InlineData(KlineInterval.OneMonth, CandleInterval.Month_1)]
        public void MapCandleInterval_AllValues_RoundTrip(KlineInterval binanceInterval, CandleInterval expected)
        {
            var mapped = BinanceMapper.MapCandleInterval(binanceInterval);
            mapped.Should().Be(expected);

            // Round-trip back
            var roundTripped = BinanceMapper.MapToBinanceCandleInterval(mapped);
            roundTripped.Should().Be(binanceInterval);
        }
    }
}
