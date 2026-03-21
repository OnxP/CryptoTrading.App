using System;
using Binance;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Exchange.BinanceAdapter;
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
        [InlineData(OrderType.Market, ExchangeOrderType.Market)]
        [InlineData(OrderType.Limit, ExchangeOrderType.Limit)]
        [InlineData(OrderType.LimitMaker, ExchangeOrderType.Limit)]
        [InlineData(OrderType.StopLossLimit, ExchangeOrderType.StopLimit)]
        [InlineData(OrderType.TakeProfitLimit, ExchangeOrderType.StopLimit)]
        public void MapOrderType_AllValues_Covered(OrderType input, ExchangeOrderType expected)
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

        [Fact]
        public void ToExchangeBalance_PreservesAllFields()
        {
            var binanceBalance = new AccountBalance("BTC", 1.5m, 0.5m);

            var result = BinanceMapper.ToExchangeBalance(binanceBalance);

            result.ExchangeId.Should().Be("Binance");
            result.Asset.Should().Be("BTC");
            result.Free.Should().Be(1.5m);
            result.Locked.Should().Be(0.5m);
            result.Total.Should().Be(2.0m);
        }

        [Theory]
        [InlineData(CandlestickInterval.Minute, CandleInterval.Minute_1)]
        [InlineData(CandlestickInterval.Minutes_3, CandleInterval.Minute_3)]
        [InlineData(CandlestickInterval.Minutes_5, CandleInterval.Minute_5)]
        [InlineData(CandlestickInterval.Minutes_15, CandleInterval.Minute_15)]
        [InlineData(CandlestickInterval.Minutes_30, CandleInterval.Minute_30)]
        [InlineData(CandlestickInterval.Hour, CandleInterval.Hour_1)]
        [InlineData(CandlestickInterval.Hours_2, CandleInterval.Hour_2)]
        [InlineData(CandlestickInterval.Hours_4, CandleInterval.Hour_4)]
        [InlineData(CandlestickInterval.Hours_6, CandleInterval.Hour_6)]
        [InlineData(CandlestickInterval.Hours_8, CandleInterval.Hour_8)]
        [InlineData(CandlestickInterval.Hours_12, CandleInterval.Hour_12)]
        [InlineData(CandlestickInterval.Day, CandleInterval.Day_1)]
        [InlineData(CandlestickInterval.Days_3, CandleInterval.Day_3)]
        [InlineData(CandlestickInterval.Week, CandleInterval.Week_1)]
        [InlineData(CandlestickInterval.Month, CandleInterval.Month_1)]
        public void MapCandleInterval_AllValues_RoundTrip(CandlestickInterval binanceInterval, CandleInterval expected)
        {
            var mapped = BinanceMapper.MapCandleInterval(binanceInterval);
            mapped.Should().Be(expected);

            // Round-trip back
            var roundTripped = BinanceMapper.MapToBinanceCandleInterval(mapped);
            roundTripped.Should().Be(binanceInterval);
        }
    }
}
