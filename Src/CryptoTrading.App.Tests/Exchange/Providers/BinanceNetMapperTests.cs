using Binance.Net.Enums;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Exchange.BinanceNet;
using FluentAssertions;
using Xunit;
using BinanceSpotOrderType = Binance.Net.Enums.SpotOrderType;

namespace CryptoTrading.App.Tests.Exchange.Providers
{
    /// <summary>
    /// Parity tests for the Binance.Net mapper. Mirrors the coverage of
    /// <c>BinanceMapperTests</c> on the bundled-SDK adapter so we can be
    /// confident the two adapters produce interchangeable neutral values
    /// while both still ship in the same repo.
    /// </summary>
    public class BinanceNetMapperTests
    {
        [Theory]
        [InlineData(OrderSide.Buy, ExchangeOrderSide.Buy)]
        [InlineData(OrderSide.Sell, ExchangeOrderSide.Sell)]
        public void MapOrderSide_AllValues_Covered(OrderSide input, ExchangeOrderSide expected)
        {
            BinanceNetMapper.MapOrderSide(input).Should().Be(expected);
        }

        [Theory]
        [InlineData(ExchangeOrderSide.Buy, OrderSide.Buy)]
        [InlineData(ExchangeOrderSide.Sell, OrderSide.Sell)]
        public void MapToBinanceOrderSide_RoundTrips(ExchangeOrderSide input, OrderSide expected)
        {
            BinanceNetMapper.MapToBinanceOrderSide(input).Should().Be(expected);
        }

        [Theory]
        [InlineData(BinanceSpotOrderType.Market, ExchangeOrderType.Market)]
        [InlineData(BinanceSpotOrderType.Limit, ExchangeOrderType.Limit)]
        [InlineData(BinanceSpotOrderType.LimitMaker, ExchangeOrderType.Limit)]
        [InlineData(BinanceSpotOrderType.StopLoss, ExchangeOrderType.StopLimit)]
        [InlineData(BinanceSpotOrderType.StopLossLimit, ExchangeOrderType.StopLimit)]
        [InlineData(BinanceSpotOrderType.TakeProfit, ExchangeOrderType.StopLimit)]
        [InlineData(BinanceSpotOrderType.TakeProfitLimit, ExchangeOrderType.StopLimit)]
        public void MapOrderType_AllValues_Covered(BinanceSpotOrderType input, ExchangeOrderType expected)
        {
            BinanceNetMapper.MapOrderType(input).Should().Be(expected);
        }

        [Theory]
        [InlineData(OrderStatus.New, ExchangeOrderStatus.New)]
        [InlineData(OrderStatus.PartiallyFilled, ExchangeOrderStatus.PartiallyFilled)]
        [InlineData(OrderStatus.Filled, ExchangeOrderStatus.Filled)]
        [InlineData(OrderStatus.Canceled, ExchangeOrderStatus.Cancelled)]
        [InlineData(OrderStatus.PendingCancel, ExchangeOrderStatus.Cancelled)]
        [InlineData(OrderStatus.Rejected, ExchangeOrderStatus.Rejected)]
        [InlineData(OrderStatus.Expired, ExchangeOrderStatus.Expired)]
        [InlineData(OrderStatus.ExpiredInMatch, ExchangeOrderStatus.Expired)]
        public void MapOrderStatus_AllValues_Covered(OrderStatus input, ExchangeOrderStatus expected)
        {
            BinanceNetMapper.MapOrderStatus(input).Should().Be(expected);
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
            var mapped = BinanceNetMapper.MapCandleInterval(binanceInterval);
            mapped.Should().Be(expected);

            // Round-trip back through the reverse map so any table drift
            // between the two directions is caught by a single test.
            var roundTripped = BinanceNetMapper.MapToBinanceKlineInterval(mapped);
            roundTripped.Should().Be(binanceInterval);
        }

        [Fact]
        public void ExchangeName_MatchesBundledAdapter()
        {
            // Consumers index balances and fee schedules by ExchangeId; if the
            // two adapters ever diverge on the ID, a system running with
            // both registered would split into "two Binances" silently.
            BinanceNetMapper.ExchangeName.Should().Be("Binance");
        }
    }
}
