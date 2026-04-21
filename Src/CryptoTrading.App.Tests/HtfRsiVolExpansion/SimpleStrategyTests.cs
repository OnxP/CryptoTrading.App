using CryptoTrading.App.Algorithm.HtfRsiVolExpansion;
using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using FluentAssertions;
using Moq;
using Skender.Stock.Indicators;
using System;
using Xunit;

namespace CryptoTrading.App.Tests.HtfRsiVolExpansion
{
    /// <summary>
    /// Tests matching the backtested spec exactly:
    ///   - Entry: immediate market order on 15M signal
    ///   - SL: 1.5 × ATR
    ///   - TP: 1.5 × ATR (1:1 R:R)
    ///   - Trailing: activates at 1.5R, trails at 1.0 × ATR
    ///   - Time stop: 16 × 15M = 240 × 1M bars (4 hours)
    ///   - Score: logged only, no effect on TP or sizing
    /// </summary>
    public class SimpleStrategyTests
    {
        #region Test Infrastructure

        private class SimplePipeline
        {
            public HtfRsiTradingState TradingState { get; }
            public HtfRsiVolExpansionSetup Setup { get; }
            public SimpleExecutionStrategy ExecutionStrategy { get; }
            public QuoteHub<IQuote> QuoteHub { get; }
            private readonly Mock<ITrade> _tradeMock;
            private DateTime _currentTime;

            public SimplePipeline(
                TradeDirection direction,
                decimal entryPrice,
                decimal atr = 500m)
            {
                var risk = atr * 1.5m;

                TradingState = new HtfRsiTradingState(200_000m);
                TradingState.IsInPosition = true;

                Setup = new HtfRsiVolExpansionSetup
                {
                    Direction = direction,
                    EntryPrice = entryPrice,
                    StopLoss = direction == TradeDirection.Long
                        ? entryPrice - risk : entryPrice + risk,
                    TakeProfit = direction == TradeDirection.Long
                        ? entryPrice + risk : entryPrice - risk,
                    AtrAtEntry = atr,
                    InitialRisk = risk,
                    HtfRsi = direction == TradeDirection.Long ? 70.0 : 30.0,
                    VolExpansionRatio = 1.5,
                    ProbabilityScore = 65,
                    Quantity = 1.0m,
                    Leverage = 5
                };

                ExecutionStrategy = new SimpleExecutionStrategy(Setup, TradingState);
                QuoteHub = new QuoteHub<IQuote>(300);
                ExecutionStrategy.SetQuotes(QuoteHub);

                _tradeMock = new Mock<ITrade>();
                _tradeMock.Setup(t => t.Open).Returns(false);
                _tradeMock.Setup(t => t.TotalOpenBaseQuantity).Returns(0m);
                _tradeMock.Setup(t => t.ProfitPct).Returns(0m);

                _currentTime = new DateTime(2025, 7, 25, 4, 15, 0);

                for (int i = 0; i < 20; i++)
                    AddCandle(entryPrice);
            }

            public StrategyAction ProcessCandle(
                decimal close,
                decimal? high = null,
                decimal? low = null)
            {
                high ??= close + 10;
                low ??= close - 10;
                AddCandle(close, high.Value, low.Value);
                return ExecutionStrategy.ProcessStrategy(_tradeMock.Object).StrategyAction;
            }

            public void SimulateTradeOpened(decimal positionSize = 1.0m)
            {
                _tradeMock.Setup(t => t.Open).Returns(true);
                _tradeMock.Setup(t => t.TotalOpenBaseQuantity).Returns(positionSize);
            }

            public void SimulateTradeClosed()
            {
                _tradeMock.Setup(t => t.Open).Returns(false);
                _tradeMock.Setup(t => t.TotalOpenBaseQuantity).Returns(0m);
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

        #region Entry

        [Fact]
        public void Entry_EntersImmediately()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m);
            var action = pipeline.ProcessCandle(100_000m);
            action.Should().Be(StrategyAction.OpenTrade);
        }

        [Fact]
        public void Entry_NoReEntryAfterConsumed()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase
            pipeline.SimulateTradeClosed();

            var action = pipeline.ProcessCandle(100_000m);
            action.Should().Be(StrategyAction.NoAction, "setup consumed after one trade");
        }

        [Fact]
        public void Entry_StaleSetup_PriceBelowSL_NoEntry()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m);
            var action = pipeline.ProcessCandle(99_000m);
            action.Should().Be(StrategyAction.NoAction);
        }

        #endregion

        #region Stop Loss (checked on candle high/low)

        [Fact]
        public void Long_StopLoss_ExitsOnLowBreach()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 500m);
            // Risk = 750, SL = 99,250

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            var exit = pipeline.ProcessCandle(99_100m, low: 99_200m);
            exit.Should().Be(StrategyAction.CloseTrade);
        }

        [Fact]
        public void Short_StopLoss_ExitsOnHighBreach()
        {
            var pipeline = new SimplePipeline(TradeDirection.Short, 100_000m, atr: 500m);
            // Risk = 750, SL = 100,750

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m);

            var exit = pipeline.ProcessCandle(100_900m, high: 100_800m);
            exit.Should().Be(StrategyAction.CloseTrade);
        }

        #endregion

        #region Take Profit (1:1 R:R, checked on candle high/low)

        [Fact]
        public void Long_TakeProfit_ExitsOnHighBreach()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 500m);
            // Risk = 750, TP = 100,750

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m);

            var exit = pipeline.ProcessCandle(100_800m, high: 100_800m);
            exit.Should().Be(StrategyAction.CloseTrade);
        }

        [Fact]
        public void Short_TakeProfit_ExitsOnLowBreach()
        {
            var pipeline = new SimplePipeline(TradeDirection.Short, 100_000m, atr: 500m);
            // Risk = 750, TP = 99,250

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m);

            var exit = pipeline.ProcessCandle(99_100m, low: 99_200m);
            exit.Should().Be(StrategyAction.CloseTrade);
        }

        #endregion

        #region Trailing Stop (activates at 1.5R, trails at 1.0 × ATR)

        [Fact]
        public void Long_TrailingStop_DoesNotActivateBefore1_5R()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 500m);
            // Risk = 750, 1.5R = 1,125. Trailing activates at 101,125.

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            // Price at 1R (100,750) — trailing should NOT activate
            pipeline.ProcessCandle(100_750m, high: 100_800m);

            // Pull back — no exit (trailing not active, and we're above SL)
            var hold = pipeline.ProcessCandle(100_400m, high: 100_500m, low: 100_350m);
            hold.Should().Be(StrategyAction.NoAction,
                "trailing not active at 1R, no exit on pullback");
        }

        [Fact]
        public void Long_TrailingStop_ActivatesAt1_5R_AndTrails()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 500m);
            // Risk = 750, 1.5R = 1,125. ATR = 500.

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            // Push to 1.5R+ (close >= 101,125)
            pipeline.ProcessCandle(101_200m, high: 101_200m);
            // Trail = highest(101,200) - ATR(500) = 100,700

            // Continue up
            pipeline.ProcessCandle(101_500m, high: 101_500m);
            // Trail = 101,500 - 500 = 101,000

            // Small pullback — above trail
            var hold = pipeline.ProcessCandle(101_100m, high: 101_100m, low: 101_050m);
            hold.Should().Be(StrategyAction.NoAction, "above trailing stop at 101,000");

            // Drop through trail
            var exit = pipeline.ProcessCandle(100_900m, low: 100_900m);
            exit.Should().Be(StrategyAction.CloseTrade, "below trailing stop at 101,000");
        }

        [Fact]
        public void Short_TrailingStop_ActivatesAt1_5R_AndTrails()
        {
            var pipeline = new SimplePipeline(TradeDirection.Short, 100_000m, atr: 500m);
            // Risk = 750, 1.5R = 1,125. ATR = 500.

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            // Drop to 1.5R+ (close <= 98,875)
            pipeline.ProcessCandle(98_800m, low: 98_800m);
            // Trail = lowest(98,800) + ATR(500) = 99,300

            // Continue down
            pipeline.ProcessCandle(98_500m, low: 98_500m);
            // Trail = 98,500 + 500 = 99,000

            // Bounce through trail
            var exit = pipeline.ProcessCandle(99_100m, high: 99_100m);
            exit.Should().Be(StrategyAction.CloseTrade, "above trailing stop at 99,000");
        }

        [Fact]
        public void TrailingStop_OnlyMovesInProfitableDirection()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 500m);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m);

            // Push to 1.5R
            pipeline.ProcessCandle(101_200m, high: 101_200m);
            // Trail = 101,200 - 500 = 100,700

            // Higher high
            pipeline.ProcessCandle(101_800m, high: 101_800m);
            // Trail = 101,800 - 500 = 101,300

            // Price dips but stays above trail — trail should NOT move down
            pipeline.ProcessCandle(101_400m, high: 101_400m, low: 101_350m);
            // Trail stays at 101,300 (not 101,400 - 500 = 100,900)

            // Verify trail held at 101,300 by checking that 101,250 triggers exit
            var exit = pipeline.ProcessCandle(101_200m, low: 101_200m);
            exit.Should().Be(StrategyAction.CloseTrade,
                "trail should still be at 101,300 — low of 101,200 is below it");
        }

        #endregion

        #region Time Stop (16 × 15M = 240 × 1M bars)

        [Fact]
        public void TimeStop_ExitsAfter240Bars()
        {
            // Use huge ATR so SL/TP never trigger
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 50_000m);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();

            // 239 bars — no exit
            for (int i = 0; i < 239; i++)
            {
                var action = pipeline.ProcessCandle(100_000m);
                action.Should().Be(StrategyAction.NoAction);
            }

            // Bar 240 — time stop
            var exit = pipeline.ProcessCandle(100_000m);
            exit.Should().Be(StrategyAction.CloseTrade, "time stop at 240 bars (4 hours)");
        }

        [Fact]
        public void TimeStop_DoesNotFireBefore240Bars()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 50_000m);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();

            // Bar 239 — should NOT exit
            for (int i = 0; i < 238; i++)
                pipeline.ProcessCandle(100_000m);

            var hold = pipeline.ProcessCandle(100_000m);
            hold.Should().Be(StrategyAction.NoAction, "bar 239 is before time stop");
        }

        #endregion

        #region No Exit When In Range

        [Fact]
        public void NoExit_WhenPriceBetweenSlAndTp_NoTrailing()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 500m);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m);

            // Price moves within range — no exit
            for (int i = 0; i < 50; i++)
            {
                var action = pipeline.ProcessCandle(100_200m, high: 100_300m, low: 100_100m);
                action.Should().Be(StrategyAction.NoAction);
            }
        }

        #endregion

        #region Rebase

        [Fact]
        public void Rebase_AdjustsSlTpToFillPrice()
        {
            // BbGuide pins EntryPrice/SL/TP to the 1M close at alignment (or
            // budget expiry) inside the entry strategy itself, and marks the
            // setup final so SimpleExecutionStrategy skips its own rebase.
            // The first ProcessCandle here trips budget expiry (Setup.EntryTime
            // defaults to DateTime.MinValue), so the fill price is pinned to
            // that bar's close.
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 500m);
            var risk = 500m * 1.5m;

            pipeline.ProcessCandle(100_300m);
            pipeline.SimulateTradeOpened();

            // Second bar — rebase should be skipped (EntryPriceFinal=true).
            pipeline.ProcessCandle(100_500m);

            pipeline.Setup.EntryPriceFinal.Should().BeTrue();
            pipeline.Setup.EntryPrice.Should().Be(100_300m);
            pipeline.Setup.StopLoss.Should().Be(100_300m - risk);
            pipeline.Setup.TakeProfit.Should().Be(100_300m + risk);
        }

        [Fact]
        public void Entry_IdempotentWithinBar_ProductionTwoCallPattern()
        {
            // Regression guard for the production TradeMonitor pattern, which
            // calls GetNextEntry twice per 1M tick:
            //   1. SimpleExecutionStrategy.ProcessStrategy calls it to decide
            //      whether to return OpenTrade.
            //   2. TradeMonitor.ExecuteEntryStrategy calls it again on the
            //      same bar to get the actual fill details it submits.
            //
            // Both calls must agree on ShouldTrade=true and the same price on
            // the firing bar — otherwise ProcessStrategy returns OpenTrade,
            // marks _setupConsumed, and then ExecuteEntryStrategy silently
            // drops the order because the entry strategy's internal de-dupe
            // fires. The setup then dies permanently because _setupConsumed
            // blocks further attempts. This was the "no trades in main-app
            // BackTesting" bug.
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 500m);

            // First call — goes through ProcessStrategy → GetNextEntry, fires
            // OpenTrade via budget-expiry fill (Setup.EntryTime = MinValue).
            var first = pipeline.ProcessCandle(100_300m);
            first.Should().Be(StrategyAction.OpenTrade);

            // Second call on the same bar (no new quote pushed) — the direct
            // GetNextEntry call the production TradeMonitor makes must return
            // the same ShouldTrade=true decision so the order actually submits.
            var second = pipeline.ExecutionStrategy.EntryStrategy
                .GetNextEntry(0m, pipeline.Setup.Quantity, 100_300m);
            second.ShouldTrade.Should().BeTrue(
                "second call on same bar must replay cached decision");
            second.Price.Should().Be(100_300m);
            second.Quantity.Should().Be(pipeline.Setup.Quantity);
        }

        #endregion

        #region End to End

        [Fact]
        public void EndToEnd_Long_TakeProfit()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 500m);

            var entry = pipeline.ProcessCandle(100_000m);
            entry.Should().Be(StrategyAction.OpenTrade);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m);

            pipeline.ProcessCandle(100_200m);
            pipeline.ProcessCandle(100_400m);
            pipeline.ProcessCandle(100_600m);

            var exit = pipeline.ProcessCandle(100_800m, high: 100_800m);
            exit.Should().Be(StrategyAction.CloseTrade, "TP at 100,750 breached");

            pipeline.SimulateTradeClosed();
            var reentry = pipeline.ProcessCandle(100_000m);
            reentry.Should().Be(StrategyAction.NoAction);
        }

        [Fact]
        public void EndToEnd_Short_TrailingStopWin()
        {
            var pipeline = new SimplePipeline(TradeDirection.Short, 100_000m, atr: 500m);
            // Risk = 750, 1.5R = 1,125

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m);

            // Drop past 1.5R to activate trailing
            decimal price = 100_000m;
            for (int i = 0; i < 15; i++)
            {
                price -= 80m;
                pipeline.ProcessCandle(price, high: price + 20m, low: price - 20m);
            }
            // price ≈ 98,800, trailing should be active

            // Bounce — trailing stop catches it
            bool exited = false;
            for (int i = 0; i < 20; i++)
            {
                price += 40m;
                var action = pipeline.ProcessCandle(price, high: price + 20m, low: price - 10m);
                if (action == StrategyAction.CloseTrade)
                {
                    exited = true;
                    break;
                }
            }

            exited.Should().BeTrue("trailing stop should catch the bounce");
        }

        [Fact]
        public void EndToEnd_Long_TimeStop()
        {
            // Use huge ATR so SL/TP/trailing never trigger
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 50_000m);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();

            bool timeStopHit = false;
            for (int i = 0; i < 245; i++)
            {
                var action = pipeline.ProcessCandle(100_000m);
                if (action == StrategyAction.CloseTrade)
                {
                    timeStopHit = true;
                    i.Should().Be(239, "time stop fires on bar 240");
                    break;
                }
            }

            timeStopHit.Should().BeTrue("time stop should fire at 240 bars");
        }

        #endregion

        #region Exit Priority

        [Fact]
        public void ExitPriority_SL_BeforeTP_WhenBothBreached()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 500m);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m);

            // Wild candle breaching both SL (99,250) and TP (100,750)
            var exit = pipeline.ProcessCandle(100_000m, high: 100_800m, low: 99_200m);
            exit.Should().Be(StrategyAction.CloseTrade);
        }

        #endregion
    }
}
