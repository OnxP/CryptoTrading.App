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
    /// Tests for the simplified execution strategy.
    /// Entry: immediate market order.
    /// Exit: hard SL and hard TP only (1:1 R:R at 1.5 × ATR).
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

            // After close, should not re-enter
            var action = pipeline.ProcessCandle(100_000m);
            action.Should().Be(StrategyAction.NoAction, "setup consumed after one trade");
        }

        [Fact]
        public void Entry_StaleSetup_PriceBelowSL_NoEntry()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m);
            // SL is at 99,250 — price already below
            var action = pipeline.ProcessCandle(99_000m);
            action.Should().Be(StrategyAction.NoAction);
        }

        #endregion

        #region Stop Loss

        [Fact]
        public void Long_StopLoss_ExitsOnLowBreach()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 500m);
            // Risk = 750, SL = 99,250

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            // Low breaches SL
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
            pipeline.ProcessCandle(100_000m); // rebase

            // High breaches SL
            var exit = pipeline.ProcessCandle(100_900m, high: 100_800m);
            exit.Should().Be(StrategyAction.CloseTrade);
        }

        #endregion

        #region Take Profit

        [Fact]
        public void Long_TakeProfit_ExitsOnHighBreach()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 500m);
            // Risk = 750, TP = 100,750

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            // High reaches TP
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
            pipeline.ProcessCandle(100_000m); // rebase

            // Low reaches TP
            var exit = pipeline.ProcessCandle(99_100m, low: 99_200m);
            exit.Should().Be(StrategyAction.CloseTrade);
        }

        #endregion

        #region No Exit In Range

        [Fact]
        public void NoExit_WhenPriceStaysBetweenSlAndTp()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 500m);

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            // Price moves within range — no exit
            for (int i = 0; i < 300; i++)
            {
                var action = pipeline.ProcessCandle(100_200m, high: 100_300m, low: 100_100m);
                action.Should().Be(StrategyAction.NoAction,
                    $"bar {i}: no time stop, no trailing — only SL/TP exits");
            }
        }

        #endregion

        #region Rebase

        [Fact]
        public void Rebase_AdjustsSlTpToFillPrice()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 500m);
            var risk = 500m * 1.5m; // 750

            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();

            // Fill at 100,300 (slippage)
            pipeline.ProcessCandle(100_300m);

            pipeline.Setup.EntryPrice.Should().Be(100_300m);
            pipeline.Setup.StopLoss.Should().Be(100_300m - risk);
            pipeline.Setup.TakeProfit.Should().Be(100_300m + risk);
        }

        #endregion

        #region End to End

        [Fact]
        public void EndToEnd_Long_TakeProfit()
        {
            var pipeline = new SimplePipeline(TradeDirection.Long, 100_000m, atr: 500m);

            // Enter
            var entry = pipeline.ProcessCandle(100_000m);
            entry.Should().Be(StrategyAction.OpenTrade);
            pipeline.SimulateTradeOpened();

            // Rebase
            pipeline.ProcessCandle(100_000m);

            // Drift up
            pipeline.ProcessCandle(100_200m);
            pipeline.ProcessCandle(100_400m);
            pipeline.ProcessCandle(100_600m);

            // TP hit at 100,750
            var exit = pipeline.ProcessCandle(100_800m, high: 100_800m);
            exit.Should().Be(StrategyAction.CloseTrade, "TP at 100,750 breached");

            // No re-entry
            pipeline.SimulateTradeClosed();
            var reentry = pipeline.ProcessCandle(100_000m);
            reentry.Should().Be(StrategyAction.NoAction);
        }

        [Fact]
        public void EndToEnd_Short_StopLoss()
        {
            var pipeline = new SimplePipeline(TradeDirection.Short, 100_000m, atr: 500m);

            // Enter
            pipeline.ProcessCandle(100_000m);
            pipeline.SimulateTradeOpened();
            pipeline.ProcessCandle(100_000m); // rebase

            // Price reverses up
            pipeline.ProcessCandle(100_200m);
            pipeline.ProcessCandle(100_500m);

            // SL hit at 100,750
            var exit = pipeline.ProcessCandle(100_800m, high: 100_800m);
            exit.Should().Be(StrategyAction.CloseTrade, "SL at 100,750 breached");
        }

        #endregion
    }
}
