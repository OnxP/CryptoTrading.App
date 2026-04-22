using Binance.Net.Enums;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Exchange.BinanceNet;
using FluentAssertions;
using Xunit;
using BinanceSpotOrderType = Binance.Net.Enums.SpotOrderType;
using BinanceFuturesOrderType = Binance.Net.Enums.FuturesOrderType;
using BinancePositionSide = Binance.Net.Enums.PositionSide;
using BinanceSideEffectType = Binance.Net.Enums.SideEffectType;
// Both Binance.Net.Enums and CryptoTrading.App.Core.Exchange declare a
// PositionSide enum; alias the neutral one so test InlineData doesn't have
// to fully qualify every case.
using NeutralPositionSide = CryptoTrading.App.Core.Exchange.PositionSide;

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

        // ---- Futures mapping coverage (PR 3) ----

        [Theory]
        [InlineData(BinanceFuturesOrderType.Market, ExchangeOrderType.Market)]
        [InlineData(BinanceFuturesOrderType.StopMarket, ExchangeOrderType.Market)]
        [InlineData(BinanceFuturesOrderType.TakeProfitMarket, ExchangeOrderType.Market)]
        [InlineData(BinanceFuturesOrderType.TrailingStopMarket, ExchangeOrderType.Market)]
        [InlineData(BinanceFuturesOrderType.Liquidation, ExchangeOrderType.Market)]
        [InlineData(BinanceFuturesOrderType.Limit, ExchangeOrderType.Limit)]
        [InlineData(BinanceFuturesOrderType.Stop, ExchangeOrderType.StopLimit)]
        [InlineData(BinanceFuturesOrderType.TakeProfit, ExchangeOrderType.StopLimit)]
        public void MapFuturesOrderType_AllValues_Covered(
            BinanceFuturesOrderType input, ExchangeOrderType expected)
        {
            BinanceNetMapper.MapFuturesOrderType(input).Should().Be(expected);
        }

        [Theory]
        [InlineData(ExchangeOrderType.Market, BinanceFuturesOrderType.Market)]
        [InlineData(ExchangeOrderType.Limit, BinanceFuturesOrderType.Limit)]
        [InlineData(ExchangeOrderType.StopLimit, BinanceFuturesOrderType.Stop)]
        public void MapToBinanceFuturesOrderType_NeutralToExchange(
            ExchangeOrderType input, BinanceFuturesOrderType expected)
        {
            BinanceNetMapper.MapToBinanceFuturesOrderType(input).Should().Be(expected);
        }

        [Theory]
        [InlineData(NeutralPositionSide.Long, BinancePositionSide.Long)]
        [InlineData(NeutralPositionSide.Short, BinancePositionSide.Short)]
        [InlineData(NeutralPositionSide.Both, BinancePositionSide.Both)]
        public void MapToBinancePositionSide_AllValues_Covered(
            NeutralPositionSide input, BinancePositionSide expected)
        {
            BinanceNetMapper.MapToBinancePositionSide(input).Should().Be(expected);
        }

        [Theory]
        [InlineData(BinancePositionSide.Long, NeutralPositionSide.Long)]
        [InlineData(BinancePositionSide.Short, NeutralPositionSide.Short)]
        [InlineData(BinancePositionSide.Both, NeutralPositionSide.Both)]
        public void MapFromBinancePositionSide_RoundTrips(
            BinancePositionSide input, NeutralPositionSide expected)
        {
            BinanceNetMapper.MapFromBinancePositionSide(input).Should().Be(expected);
        }

        // ---- Margin mapping coverage (PR 4) ----

        [Fact]
        public void MapToBinanceSideEffectType_None_ReturnsNull()
        {
            // None must project to null so the caller can omit the
            // sideEffectType parameter on PlaceMarginOrderAsync — Binance
            // defaults to NO_SIDE_EFFECT when absent.
            BinanceNetMapper.MapToBinanceSideEffectType(MarginSideEffect.None).Should().BeNull();
        }

        [Theory]
        [InlineData(MarginSideEffect.AutoBorrow, BinanceSideEffectType.MarginBuy)]
        [InlineData(MarginSideEffect.AutoRepay, BinanceSideEffectType.AutoRepay)]
        public void MapToBinanceSideEffectType_ExplicitValues(
            MarginSideEffect input, BinanceSideEffectType expected)
        {
            BinanceNetMapper.MapToBinanceSideEffectType(input).Should().Be(expected);
        }
    }
}
