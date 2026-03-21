using System;
using Bitfinex.Net.Enums;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Exchange.Bitfinex;
using FluentAssertions;
using Xunit;

namespace CryptoTrading.App.Tests.Exchange.Providers
{
    public class BitfinexMapperTests
    {
        [Theory]
        [InlineData("BTCUSDT", "tBTCUST")]
        [InlineData("ETHUSDT", "tETHUST")]
        [InlineData("BTCUSD", "tBTCUSD")]
        public void ToBitfinexSymbol_ConvertsCorrectly(string standard, string expected)
        {
            BitfinexMapper.ToBitfinexSymbol(standard).Should().Be(expected);
        }

        [Theory]
        [InlineData("tBTCUST", "BTCUSDT")]
        [InlineData("tETHUST", "ETHUSDT")]
        [InlineData("tBTCUSD", "BTCUSD")]
        public void ToStandardSymbol_ConvertsCorrectly(string bitfinex, string expected)
        {
            BitfinexMapper.ToStandardSymbol(bitfinex).Should().Be(expected);
        }

        [Fact]
        public void SymbolConversion_RoundTrips()
        {
            var original = "BTCUSDT";
            var bfx = BitfinexMapper.ToBitfinexSymbol(original);
            var back = BitfinexMapper.ToStandardSymbol(bfx);
            back.Should().Be(original);
        }

        [Theory]
        [InlineData(OrderSide.Buy, ExchangeOrderSide.Buy)]
        [InlineData(OrderSide.Sell, ExchangeOrderSide.Sell)]
        public void MapOrderSide_AllValues_Covered(OrderSide input, ExchangeOrderSide expected)
        {
            BitfinexMapper.MapOrderSide(input).Should().Be(expected);
        }

        [Theory]
        [InlineData(ExchangeOrderSide.Buy, OrderSide.Buy)]
        [InlineData(ExchangeOrderSide.Sell, OrderSide.Sell)]
        public void MapToBitfinexOrderSide_RoundTrips(ExchangeOrderSide input, OrderSide expected)
        {
            BitfinexMapper.MapToBitfinexOrderSide(input).Should().Be(expected);
        }

        [Theory]
        [InlineData(OrderStatus.Active, ExchangeOrderStatus.New)]
        [InlineData(OrderStatus.PartiallyFilled, ExchangeOrderStatus.PartiallyFilled)]
        [InlineData(OrderStatus.Executed, ExchangeOrderStatus.Filled)]
        [InlineData(OrderStatus.Canceled, ExchangeOrderStatus.Cancelled)]
        public void MapOrderStatus_AllValues_Covered(OrderStatus input, ExchangeOrderStatus expected)
        {
            BitfinexMapper.MapOrderStatus(input).Should().Be(expected);
        }

        [Theory]
        [InlineData(CandleInterval.Minute_1, KlineInterval.OneMinute)]
        [InlineData(CandleInterval.Minute_5, KlineInterval.FiveMinutes)]
        [InlineData(CandleInterval.Minute_15, KlineInterval.FifteenMinutes)]
        [InlineData(CandleInterval.Hour_1, KlineInterval.OneHour)]
        [InlineData(CandleInterval.Day_1, KlineInterval.OneDay)]
        [InlineData(CandleInterval.Week_1, KlineInterval.SevenDays)]
        [InlineData(CandleInterval.Month_1, KlineInterval.OneMonth)]
        public void MapToBitfinexCandleInterval_CorrectValues(CandleInterval interval, KlineInterval expected)
        {
            BitfinexMapper.MapToBitfinexCandleInterval(interval).Should().Be(expected);
        }

        [Theory]
        [InlineData(CandleInterval.Minute_1, 1)]
        [InlineData(CandleInterval.Minute_5, 5)]
        [InlineData(CandleInterval.Hour_1, 60)]
        [InlineData(CandleInterval.Day_1, 1440)]
        public void ComputeCloseTime_AddsCorrectDuration(CandleInterval interval, int expectedMinutes)
        {
            var openTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var closeTime = BitfinexMapper.ComputeCloseTime(openTime, interval);
            closeTime.Should().Be(openTime.AddMinutes(expectedMinutes));
        }
    }
}
