using CryptoTrading.App.Algorithm.HtfRsiVolExpansion;
using FluentAssertions;
using Xunit;

namespace CryptoTrading.App.Tests.HtfRsiVolExpansion
{
    public class TradingStateTests
    {
        #region CandlesSinceLastExit Overflow Fix

        [Fact]
        public void CandlesSinceLastExit_InitializesAboveMinGap()
        {
            var state = new HtfRsiTradingState(10_000m);

            state.CandlesSinceLastExit.Should().Be(100,
                "initial value should be large enough to allow trading immediately");
            state.CanTrade.Should().BeTrue();
        }

        [Fact]
        public void OnNewCandle_DoesNotOverflow()
        {
            var state = new HtfRsiTradingState(10_000m);

            // Simulate many candles — should cap at 10000, never overflow
            for (int i = 0; i < 20_000; i++)
                state.OnNewCandle();

            state.CandlesSinceLastExit.Should().Be(10_000,
                "counter should cap at 10000 to prevent overflow");
            state.CanTrade.Should().BeTrue();
        }

        [Fact]
        public void OnNewCandle_DoesNotIncrementWhileInPosition()
        {
            var state = new HtfRsiTradingState(10_000m);
            state.IsInPosition = true;
            state.CandlesSinceLastExit = 0;

            state.OnNewCandle();

            state.CandlesSinceLastExit.Should().Be(0,
                "gap counter should not advance while in a position");
        }

        #endregion

        #region Trade Gap

        [Fact]
        public void RecordTradeComplete_ResetsGapCounter()
        {
            var state = new HtfRsiTradingState(10_000m);
            state.IsInPosition = true;
            state.CandlesSinceLastExit = 50;

            state.RecordTradeComplete("TakeProfit", 100m);

            state.CandlesSinceLastExit.Should().Be(0);
            state.IsInPosition.Should().BeFalse();
        }

        [Fact]
        public void CanTrade_FalseWhenGapTooSmall()
        {
            var state = new HtfRsiTradingState(10_000m);
            state.IsInPosition = false;
            state.CandlesSinceLastExit = 3; // Below MinGapCandles (9)

            state.CanTrade.Should().BeFalse();
        }

        [Fact]
        public void CanTrade_TrueWhenGapSufficient()
        {
            var state = new HtfRsiTradingState(10_000m);
            state.IsInPosition = false;
            state.CandlesSinceLastExit = 9; // Exactly MinGapCandles

            state.CanTrade.Should().BeTrue();
        }

        #endregion

        #region Cooldown

        [Fact]
        public void ThreeConsecutiveLosses_TriggersCooldown()
        {
            var state = new HtfRsiTradingState(10_000m);

            state.IsInPosition = true;
            state.RecordTradeComplete("StopLoss", -100m);
            state.IsInPosition = true;
            state.RecordTradeComplete("StopLoss", -100m);
            state.IsInPosition = true;
            state.RecordTradeComplete("StopLoss", -100m);

            state.ConsecutiveLosses.Should().Be(3);
            state.CooldownCandlesRemaining.Should().Be(16);
            state.CanTrade.Should().BeFalse("cooldown should prevent trading");
        }

        [Fact]
        public void CooldownDecrementsOnNewCandle()
        {
            var state = new HtfRsiTradingState(10_000m);
            state.CooldownCandlesRemaining = 3;
            state.CandlesSinceLastExit = 100;

            state.OnNewCandle();
            state.CooldownCandlesRemaining.Should().Be(2);

            state.OnNewCandle();
            state.CooldownCandlesRemaining.Should().Be(1);

            state.OnNewCandle();
            state.CooldownCandlesRemaining.Should().Be(0);
            state.CanTrade.Should().BeTrue("cooldown expired");
        }

        [Fact]
        public void WinResetsConsecutiveLosses()
        {
            var state = new HtfRsiTradingState(10_000m);

            state.IsInPosition = true;
            state.RecordTradeComplete("StopLoss", -100m);
            state.IsInPosition = true;
            state.RecordTradeComplete("StopLoss", -100m);
            state.ConsecutiveLosses.Should().Be(2);

            state.IsInPosition = true;
            state.RecordTradeComplete("TakeProfit", 200m);
            state.ConsecutiveLosses.Should().Be(0);
        }

        #endregion

        #region Performance Tracking

        [Fact]
        public void RecordTradeComplete_UpdatesEquityAndPeak()
        {
            var state = new HtfRsiTradingState(10_000m);

            state.IsInPosition = true;
            state.RecordTradeComplete("TakeProfit", 500m);

            state.CurrentEquity.Should().Be(10_500m);
            state.PeakEquity.Should().Be(10_500m);

            state.IsInPosition = true;
            state.RecordTradeComplete("StopLoss", -200m);

            state.CurrentEquity.Should().Be(10_300m);
            state.PeakEquity.Should().Be(10_500m, "peak should not decrease");
        }

        [Fact]
        public void RecentWinRate_DefaultsWhenUnderFiveTrades()
        {
            var state = new HtfRsiTradingState(10_000m);
            state.RecentWinRate.Should().Be(0.6, "default when < 5 trades");
        }

        [Fact]
        public void RecentWinRate_CalculatesAfterFiveTrades()
        {
            var state = new HtfRsiTradingState(10_000m);

            // 3 wins, 2 losses
            state.IsInPosition = true;
            state.RecordTradeComplete("TakeProfit", 100m);
            state.IsInPosition = true;
            state.RecordTradeComplete("StopLoss", -50m);
            state.IsInPosition = true;
            state.RecordTradeComplete("TakeProfit", 100m);
            state.IsInPosition = true;
            state.RecordTradeComplete("TakeProfit", 100m);
            state.IsInPosition = true;
            state.RecordTradeComplete("StopLoss", -50m);

            state.RecentWinRate.Should().Be(0.6); // 3/5
        }

        #endregion
    }
}
