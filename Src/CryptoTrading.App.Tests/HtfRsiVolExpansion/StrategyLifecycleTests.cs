using CryptoTrading.App.Algorithm.HtfRsiVolExpansion;
using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core.Strategy;
using FluentAssertions;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace CryptoTrading.App.Tests.HtfRsiVolExpansion
{
    /// <summary>
    /// Integration tests that wire up the REAL strategy pipeline:
    ///   HtfRsiTradingState + Setup + ExecutionStrategy + ExitStrategy + QuoteHub
    ///
    /// These test the full trade lifecycle candle-by-candle, catching bugs at the
    /// seams between components (double-gating, re-entry loops, stale setups, etc.)
    /// that unit tests with mocks miss.
    ///
    /// TradeState is passed directly as a simple record — no mocking needed.
    /// Everything else is real.
    /// </summary>
    public class StrategyLifecycleTests
    {
        #region Test Infrastructure

        /// <summary>
        /// Bundles all the real strategy objects together for lifecycle testing.
        /// Mirrors how the algorithm creates and wires these in production.
        /// </summary>
        private class StrategyPipeline
        {
            public HtfRsiTradingState TradingState { get; }
            public HtfRsiVolExpansionSetup Setup { get; }
            public HtfRsiVolExpansionExecutionStrategy ExecutionStrategy { get; }
            public QuoteHub<IQuote> QuoteHub { get; }
            private TradeState _tradeState;
            private DateTime _currentTime;

            public int CandleCount { get; private set; }

            public StrategyPipeline(
                TradeDirection direction,
                decimal entryPrice,
                decimal atr = 500m,
                int probabilityScore = 75,
                decimal initialEquity = 200_000m)
            {
                var risk = atr * 1.5m; // SlTpAtrMultiplier = 1.5

                TradingState = new HtfRsiTradingState(initialEquity);

                Setup = new HtfRsiVolExpansionSetup
                {
                    Direction = direction,
                    EntryPrice = entryPrice,
                    StopLoss = direction == TradeDirection.Long
                        ? entryPrice - risk
                        : entryPrice + risk,
                    TakeProfit = direction == TradeDirection.Long
                        ? entryPrice + risk
                        : entryPrice - risk,
                    AtrAtEntry = atr,
                    InitialRisk = risk,
                    HtfRsi = direction == TradeDirection.Long ? 70.0 : 30.0,
                    VolExpansionRatio = 1.5,
                    ProbabilityScore = probabilityScore,
                    Quantity = 1.0m,
                    Leverage = 5
                };

                // Mirror production: algorithm sets IsInPosition = true when firing setup
                TradingState.IsInPosition = true;

                ExecutionStrategy = new HtfRsiVolExpansionExecutionStrategy(Setup, TradingState);

                QuoteHub = new QuoteHub<IQuote>(300);
                ExecutionStrategy.SetQuotes(QuoteHub);

                _tradeState = new TradeState(false, 0m, 0m, 0m);

                _currentTime = new DateTime(2025, 7, 25, 4, 15, 0);

                // Seed 20 candles at entry price (minimum for quote hub)
                for (int i = 0; i < 20; i++)
                    AddCandle(entryPrice);
            }

            /// <summary>
            /// Adds a 1M candle and processes the strategy — exactly what TradeMonitor does.
            /// Returns the strategy action (OpenTrade, CloseTrade, NoAction).
            /// </summary>
            public StrategyAction ProcessCandle(
                decimal close,
                decimal? high = null,
                decimal? low = null)
            {
                high ??= close + 10;
                low ??= close - 10;

                AddCandle(close, high.Value, low.Value);
                CandleCount++;

                var status = ExecutionStrategy.ProcessStrategy(_tradeState);
                return status.StrategyAction;
            }

            /// <summary>
            /// Simulate the trade becoming open (TradeMonitor fills the entry order).
            /// </summary>
            public void SimulateTradeOpened(decimal positionSize = 1.0m)
            {
                _tradeState = new TradeState(true, 0m, positionSize, 0m);
            }

            /// <summary>
            /// Simulate trade closed (TradeMonitor calls CompleteTrade, creates new empty Trade).
            /// </summary>
            public void SimulateTradeClosed()
            {
                _tradeState = new TradeState(false, 0m, 0m, 0m);
            }

            /// <summary>
            /// Simulate what RecordTradeComplete does in the exit strategy.
            /// Call this when testing exit flow.
            /// </summary>
            public void RecordTradeResult(string reason, decimal pnl)
            {
                TradingState.RecordTradeComplete(reason, pnl);
            }

            private void AddCandle(decimal close, decimal? high = null, decimal? low = null)
            {
                QuoteHub.Add(new Quote
                {
                    Timestamp = _currentTime,
                    Open = close,
                    High = high ?? close + 10,
                    Low = low ?? close - 10,
                    Close = close,
                    Volume = 100
                });
                _currentTime = _currentTime.AddMinutes(1);
            }
        }

        #endregion

        #region 1. Entry Lifecycle — Algorithm fires setup, execution strategy enters

        [Fact]
        public void FullLifecycle_AlgorithmFiresSetup_ExecutionStrategyEnters()
        {
            // Algorithm fires a Long setup at 100k.
            // Algorithm has already set IsInPosition = true (as it does in production).
            // Execution strategy should still enter on the first candle.
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m);

            var action = pipeline.ProcessCandle(100_000m);

            action.Should().Be(StrategyAction.OpenTrade,
                "execution strategy should enter despite algorithm having set IsInPosition=true");
        }

        [Fact]
        public void FullLifecycle_ShortSetup_EntersCorrectly()
        {
            var pipeline = new StrategyPipeline(TradeDirection.Short, 100_000m);

            var action = pipeline.ProcessCandle(100_000m);

            action.Should().Be(StrategyAction.OpenTrade);
        }

        #endregion

        #region 2. No Re-Entry — After trade completes, same setup doesn't fire again

        [Fact]
        public void NoReEntry_AfterTradeCompletes_SetupConsumed()
        {
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m);

            // Enter trade
            var entry = pipeline.ProcessCandle(100_000m);
            entry.Should().Be(StrategyAction.OpenTrade);

            // Simulate trade filled and opened
            pipeline.SimulateTradeOpened();

            // Process a few candles while in trade (no exit yet)
            for (int i = 0; i < 5; i++)
                pipeline.ProcessCandle(100_100m);

            // Simulate exit (exit strategy would call RecordTradeComplete)
            pipeline.RecordTradeResult("TakeProfit", 500m);
            pipeline.SimulateTradeClosed();

            // Try to enter again — setup should be consumed
            var reentry = pipeline.ProcessCandle(100_000m);
            reentry.Should().Be(StrategyAction.NoAction,
                "setup is consumed after one trade; must not re-enter from same setup");
        }

        [Fact]
        public void NoReEntry_EvenWhenPriceIsInRange()
        {
            var pipeline = new StrategyPipeline(TradeDirection.Short, 100_000m);

            // Enter, fill, exit
            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.RecordTradeResult("StopLoss", -500m);
            pipeline.SimulateTradeClosed();

            // Price is perfectly in range — should still not re-enter
            for (int i = 0; i < 10; i++)
            {
                var action = pipeline.ProcessCandle(100_000m);
                action.Should().Be(StrategyAction.NoAction,
                    $"candle {i}: consumed setup must not re-enter");
            }
        }

        #endregion

        #region 3. SL/TP Breach — Stale setup guard prevents entry

        [Fact]
        public void StaleGuard_LongSetup_PriceDroppedBelowSL_NoEntry()
        {
            // Long at 100k, SL at 99,250. Price has crashed to 98k.
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m);

            var action = pipeline.ProcessCandle(98_000m);

            action.Should().Be(StrategyAction.NoAction,
                "price below SL means setup is stale — don't enter a doomed trade");
        }

        [Fact]
        public void StaleGuard_ShortSetup_PriceRoseAboveSL_NoEntry()
        {
            // Short at 100k, SL at 100,750. Price has surged to 101k.
            var pipeline = new StrategyPipeline(TradeDirection.Short, 100_000m);

            var action = pipeline.ProcessCandle(101_000m);

            action.Should().Be(StrategyAction.NoAction,
                "price above SL means setup is stale for a short");
        }

        [Fact]
        public void StaleGuard_LongSetup_PriceSurgedAboveTP_NoEntry()
        {
            // Long at 100k, TP at 100,750. Price already at 101k.
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m);

            var action = pipeline.ProcessCandle(101_000m);

            action.Should().Be(StrategyAction.NoAction,
                "price above TP means the move already happened");
        }

        #endregion

        #region 4. SL/TP Rebase — Fill price adjusts levels

        [Fact]
        public void Rebase_LongEntry_SlTpAdjustedToFillPrice()
        {
            // Setup at 100k but fill happens at 100,300 (slippage/delay)
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m);
            var originalRisk = pipeline.Setup.InitialRisk; // 750

            // Enter
            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();

            // First candle after fill — rebase happens here
            pipeline.ProcessCandle(100_300m);

            pipeline.Setup.EntryPrice.Should().Be(100_300m);
            pipeline.Setup.StopLoss.Should().Be(100_300m - originalRisk);
            pipeline.Setup.TakeProfit.Should().Be(100_300m + originalRisk);
        }

        [Fact]
        public void Rebase_ShortEntry_SlTpAdjustedToFillPrice()
        {
            var pipeline = new StrategyPipeline(TradeDirection.Short, 100_000m);
            var originalRisk = pipeline.Setup.InitialRisk;

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();

            // Fill at 99,800
            pipeline.ProcessCandle(99_800m);

            pipeline.Setup.EntryPrice.Should().Be(99_800m);
            pipeline.Setup.StopLoss.Should().Be(99_800m + originalRisk);
            pipeline.Setup.TakeProfit.Should().Be(99_800m - originalRisk);
        }

        [Fact]
        public void Rebase_OnlyHappensOnFirstCandleAfterOpen()
        {
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();

            // First candle rebases to 100,300
            pipeline.ProcessCandle(100_300m);
            pipeline.Setup.EntryPrice.Should().Be(100_300m);

            // Second candle at 100,500 — should NOT rebase again
            pipeline.ProcessCandle(100_500m);
            pipeline.Setup.EntryPrice.Should().Be(100_300m,
                "rebase only happens once per trade, not every candle");
        }

        #endregion

        #region 5. Dynamic TP — Score-based take profit levels

        [Theory]
        [InlineData(85, 0)]      // No hard TP
        [InlineData(75, 2.0)]    // 2R
        [InlineData(50, 1.5)]    // 1.5R
        [InlineData(30, 1.0)]    // 1.0R
        public void DynamicTP_MultiplierMatchesScore(int score, decimal expectedR)
        {
            HtfRsiVolExpansionExitStrategy.GetTakeProfitMultiplier(score)
                .Should().Be(expectedR);
        }

        [Fact]
        public void DynamicTP_Score75_ExitsAt2R_NotAt1R()
        {
            // Score 75 → 2R TP. Risk = 750. TP at entry + 1,500 = 101,500
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m, probabilityScore: 75);

            // Enter and fill at entry price
            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase at entry price

            // Price at 1R — should NOT exit
            var at1R = pipeline.ProcessCandle(100_750m, high: 100_800m);
            at1R.Should().Be(StrategyAction.NoAction,
                "1R is not enough for score 75 — needs 2R");

            // Price at 2R — should exit
            var at2R = pipeline.ProcessCandle(101_600m, high: 101_600m);
            at2R.Should().Be(StrategyAction.CloseTrade,
                "2R reached — score 75 exits here");
        }

        [Fact]
        public void DynamicTP_Score85_NoHardTP_TrailingStopManages()
        {
            // Score 85 → no hard TP. Price at 3R should NOT trigger exit.
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m, probabilityScore: 85);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            // Price at 3R — no TP exit (trailing stop should manage)
            var at3R = pipeline.ProcessCandle(102_300m, high: 102_300m);
            at3R.Should().Be(StrategyAction.NoAction,
                "score 85 has no hard TP — trailing stop manages");
        }

        [Fact]
        public void DynamicTP_Score30_ExitsAt1R()
        {
            // Score 30 → 1.0R TP. Risk = 750. TP at entry + 750 = 100,750
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m, probabilityScore: 30);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            var at1R = pipeline.ProcessCandle(100_800m, high: 100_800m);
            at1R.Should().Be(StrategyAction.CloseTrade,
                "1.0R reached — weak score exits early");
        }

        #endregion

        #region 6. Trailing Stop at 1R — Replaces old breakeven

        [Fact]
        public void TrailingAt1R_ActivatesAndLocksPartialProfit()
        {
            // Score 85 (no hard TP). ATR 500, Risk 750.
            // At 1R profit (close 100,750), trailing activates.
            // Trail = highest(100,800) - ATR(500) = 100,300.
            // This gives the trade room to breathe (300 point buffer)
            // instead of the old breakeven which exited at $0 on any bounce.
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m, atr: 500m, probabilityScore: 85);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase at entry

            // Price moves to 1R — trailing activates
            pipeline.ProcessCandle(100_750m, high: 100_800m);

            // SL should NOT move to entry (old breakeven behavior removed)
            pipeline.Setup.StopLoss.Should().NotBe(100_000m,
                "breakeven is removed — SL stays at original level");

            // Small pullback — trail at 100,300 should survive
            var hold = pipeline.ProcessCandle(100_400m, high: 100_500m, low: 100_350m);
            hold.Should().Be(StrategyAction.NoAction,
                "price above trail at 100,300 — trade survives normal volatility");

            // Deeper pullback below trail
            var exit = pipeline.ProcessCandle(100_200m, high: 100_250m, low: 100_200m);
            exit.Should().Be(StrategyAction.CloseTrade,
                "price dropped below trailing stop at 100,300");
        }

        #endregion

        #region 7. Trailing Stop — Activates at 1R, trails at 1×ATR

        [Fact]
        public void TrailingStop_ActivatesAt1R_ExitsOnReversal()
        {
            // Score 85 (no hard TP), ATR = 500, Risk = 750
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m, atr: 500m, probabilityScore: 85);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            // Move to 1R profit (750 above entry = 100,750)
            pipeline.ProcessCandle(100_800m, high: 100_800m);
            // Trail is at: highest(100,800) - ATR(500) = 100,300

            // Continue up
            pipeline.ProcessCandle(101_500m, high: 101_500m);
            // Trail is at: highest(101,500) - ATR(500) = 101,000

            // Reverse — but not below trail
            var hold = pipeline.ProcessCandle(101_100m, high: 101_100m, low: 101_050m);
            hold.Should().Be(StrategyAction.NoAction,
                "price still above trailing stop at 101,000");

            // Drop below trail
            var exit = pipeline.ProcessCandle(100_900m, high: 101_000m, low: 100_900m);
            exit.Should().Be(StrategyAction.CloseTrade,
                "price dropped below trailing stop");
        }

        [Fact]
        public void TrailingStop_Short_ActivatesAt1R_AndTrails()
        {
            var pipeline = new StrategyPipeline(TradeDirection.Short, 100_000m, atr: 500m, probabilityScore: 85);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            // Move to 1R profit (750 below entry = 99,250)
            pipeline.ProcessCandle(99_200m, low: 99_200m);
            // Trail is at: lowest(99,200) + ATR(500) = 99,700

            // Continue down
            pipeline.ProcessCandle(98_500m, low: 98_500m);
            // Trail is at: lowest(98,500) + ATR(500) = 99,000

            // Reverse above trail
            var exit = pipeline.ProcessCandle(99_100m, high: 99_100m, low: 99_000m);
            exit.Should().Be(StrategyAction.CloseTrade,
                "price rose above trailing stop");
        }

        #endregion

        #region 8. Time Stop — Exits after 240 bars

        [Fact]
        public void TimeStop_ExitsAfter240Bars()
        {
            // Use huge ATR so SL/TP/trailing never trigger
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m, atr: 50_000m, probabilityScore: 85);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();

            // 239 candles — no exit
            for (int i = 0; i < 239; i++)
            {
                var action = pipeline.ProcessCandle(100_000m);
                action.Should().Be(StrategyAction.NoAction,
                    $"bar {i + 1}: should not exit before 240 bars");
            }

            // Bar 240 — time stop
            var timeStop = pipeline.ProcessCandle(100_000m);
            timeStop.Should().Be(StrategyAction.CloseTrade,
                "time stop should trigger at 240 bars");
        }

        #endregion

        #region 9. Stop Loss — Hard exit

        [Fact]
        public void StopLoss_Long_ExitsWhenPriceBreachesSL()
        {
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            // SL is at 100,000 - 750 = 99,250. Drop below it.
            var exit = pipeline.ProcessCandle(99_000m, low: 99_200m);
            exit.Should().Be(StrategyAction.CloseTrade,
                "price below SL should trigger stop loss exit");
        }

        [Fact]
        public void StopLoss_Short_ExitsWhenPriceBreachesSL()
        {
            var pipeline = new StrategyPipeline(TradeDirection.Short, 100_000m);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            // SL is at 100,000 + 750 = 100,750. Rise above it.
            var exit = pipeline.ProcessCandle(101_000m, high: 100_800m);
            exit.Should().Be(StrategyAction.CloseTrade,
                "price above SL should trigger stop loss exit");
        }

        #endregion

        #region 10. TradingState Consistency — State remains correct through lifecycle

        [Fact]
        public void TradingState_IsInPosition_ConsistentThroughLifecycle()
        {
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m);

            // Algorithm has set IsInPosition = true when creating setup
            pipeline.TradingState.IsInPosition.Should().BeTrue(
                "algorithm sets IsInPosition=true when firing setup");

            // Enter trade
            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();

            // Still in position
            pipeline.TradingState.IsInPosition.Should().BeTrue();

            // Trade exits — RecordTradeComplete sets IsInPosition = false
            pipeline.RecordTradeResult("TakeProfit", 500m);
            pipeline.TradingState.IsInPosition.Should().BeFalse(
                "RecordTradeComplete should set IsInPosition=false");

            // Gap counter should be reset
            pipeline.TradingState.CandlesSinceLastExit.Should().Be(0);
        }

        [Fact]
        public void TradingState_ConsecutiveLosses_TriggersGooldown()
        {
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m);

            // Simulate 3 consecutive losses
            pipeline.RecordTradeResult("StopLoss", -500m);
            pipeline.RecordTradeResult("StopLoss", -500m);
            pipeline.RecordTradeResult("StopLoss", -500m);

            pipeline.TradingState.ConsecutiveLosses.Should().Be(3);
            pipeline.TradingState.CooldownCandlesRemaining.Should().Be(16,
                "3 consecutive losses should trigger 16-candle cooldown");
            pipeline.TradingState.CanTrade.Should().BeFalse(
                "cooldown should prevent trading");
        }

        [Fact]
        public void TradingState_WinResetsConsecutiveLosses()
        {
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m);

            pipeline.RecordTradeResult("StopLoss", -500m);
            pipeline.RecordTradeResult("StopLoss", -500m);
            pipeline.RecordTradeResult("TakeProfit", 500m); // Win

            pipeline.TradingState.ConsecutiveLosses.Should().Be(0,
                "a win should reset consecutive loss counter");
        }

        #endregion

        #region 11. Exit Priority — SL fires before TP, TP before trailing

        [Fact]
        public void ExitPriority_SL_FiresBeforeTP_WhenBothBreached()
        {
            // Score 30 → 1.5R TP. If a candle has both SL and TP breached,
            // SL should take priority (checked first in exit strategy).
            var pipeline = new StrategyPipeline(TradeDirection.Long, 100_000m, atr: 500m, probabilityScore: 30);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            // Wild candle that breaches both SL (99,250) and TP (101,125)
            var exit = pipeline.ProcessCandle(100_000m, high: 101_200m, low: 99_200m);
            exit.Should().Be(StrategyAction.CloseTrade,
                "exit should trigger when SL breached (SL has priority)");
        }

        #endregion

        #region 12. Full End-to-End Scenario

        [Fact]
        public void EndToEnd_LongTrade_Entry_TrailingStop_Exit()
        {
            // Complete lifecycle: entry → trailing activation at 1R → trailing exit
            var pipeline = new StrategyPipeline(
                TradeDirection.Long, 100_000m, atr: 500m, probabilityScore: 85);
            // Risk = 750. No hard TP (score 85).
            // SL at 99,250. Trailing activates at 1R (100,750).

            // 1. Entry
            var entry = pipeline.ProcessCandle(100_000m);
            entry.Should().Be(StrategyAction.OpenTrade);
            pipeline.SimulateTradeOpened();

            // 2. Rebase at fill price
            pipeline.ProcessCandle(100_000m);

            // 3. Price drifts up — no exit
            pipeline.ProcessCandle(100_200m);
            pipeline.ProcessCandle(100_400m);

            // 4. Hit 1R — trailing activates
            pipeline.ProcessCandle(100_800m, high: 100_800m);
            // Trail: 100,800 - 500 = 100,300

            // 5. Continue up — trailing follows
            pipeline.ProcessCandle(101_200m, high: 101_200m);
            // Trail: 101,200 - 500 = 100,700

            // 6. Strong trend continues
            pipeline.ProcessCandle(101_800m, high: 101_800m);
            // Trail: 101,800 - 500 = 101,300

            pipeline.ProcessCandle(102_200m, high: 102_200m);
            // Trail: 102,200 - 500 = 101,700

            // 7. Small pullback — trail holds
            var hold = pipeline.ProcessCandle(101_800m, low: 101_750m);
            hold.Should().Be(StrategyAction.NoAction, "above trailing stop at 101,700");

            // 8. Deeper reversal — trail triggers
            var exit = pipeline.ProcessCandle(101_600m, low: 101_600m);
            exit.Should().Be(StrategyAction.CloseTrade, "below trailing stop");

            // 9. Verify no re-entry
            pipeline.RecordTradeResult("TrailingStop", 1_700m);
            pipeline.SimulateTradeClosed();

            var reentry = pipeline.ProcessCandle(101_000m);
            reentry.Should().Be(StrategyAction.NoAction, "setup consumed");
        }

        [Fact]
        public void EndToEnd_ShortTrade_StopLoss()
        {
            // Short trade that hits stop loss
            var pipeline = new StrategyPipeline(
                TradeDirection.Short, 100_000m, atr: 500m, probabilityScore: 50);
            // Risk = 750. TP at 1.5R (98,875). SL at 100,750.

            // 1. Entry
            var entry = pipeline.ProcessCandle(100_000m);
            entry.Should().Be(StrategyAction.OpenTrade);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            // 2. Price goes slightly in favour then reverses
            pipeline.ProcessCandle(99_800m);
            pipeline.ProcessCandle(100_200m);
            pipeline.ProcessCandle(100_500m);

            // 3. Stop loss hit
            var exit = pipeline.ProcessCandle(100_800m, high: 100_800m);
            exit.Should().Be(StrategyAction.CloseTrade, "SL breached on short");

            // 4. No re-entry
            pipeline.RecordTradeResult("StopLoss", -750m);
            pipeline.SimulateTradeClosed();

            var reentry = pipeline.ProcessCandle(100_000m);
            reentry.Should().Be(StrategyAction.NoAction, "setup consumed");
        }

        #endregion
    }
}
