using CryptoTrading.App.Algorithm.HtfRsiVolExpansion;
using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core.Strategy;
using FluentAssertions;
using Skender.Stock.Indicators;
using Xunit;

namespace CryptoTrading.App.Tests.HtfRsiVolExpansion
{
    public class ExecutionStrategyTests
    {
        private HtfRsiVolExpansionSetup CreateSetup(
            TradeDirection direction = TradeDirection.Long,
            decimal entryPrice = 100_000m,
            decimal atr = 500m,
            decimal atrMultiplier = 1.5m)
        {
            var risk = atr * atrMultiplier;
            return new HtfRsiVolExpansionSetup
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
                ProbabilityScore = 75,
                Quantity = 1.0m,
                Leverage = 5
            };
        }

        private (HtfRsiVolExpansionExecutionStrategy strategy, QuoteHub<IQuote> quoteHub, HtfRsiTradingState tradingState)
            CreateStrategy(HtfRsiVolExpansionSetup setup, HtfRsiTradingState tradingState = null)
        {
            tradingState ??= new HtfRsiTradingState(100_000m);
            var strategy = new HtfRsiVolExpansionExecutionStrategy(setup, tradingState);
            var quoteHub = new QuoteHub<IQuote>(100);
            strategy.SetQuotes(quoteHub);
            return (strategy, quoteHub, tradingState);
        }

        private void FillQuoteHub(QuoteHub<IQuote> quoteHub, decimal price, int count = 20)
        {
            var baseTime = new DateTime(2025, 7, 1, 0, 0, 0);
            for (int i = 0; i < count; i++)
            {
                quoteHub.Add(new Quote
                {
                    Timestamp = baseTime.AddMinutes(i),
                    Open = price,
                    High = price + 10,
                    Low = price - 10,
                    Close = price,
                    Volume = 100
                });
            }
        }

        #region SL/TP Breach Detection (Stale Setup Guard)

        [Fact]
        public void Long_PriceBelowStopLoss_ReturnsNoAction()
        {
            // Long setup at 100k, SL at 99,250 (100k - 500*1.5)
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m);
            var (strategy, quoteHub, _) = CreateStrategy(setup);

            // Price dropped below SL
            FillQuoteHub(quoteHub, 99_000m);

            var tradeState = new TradeState(false, 0m, 0m, 0m);
            var result = strategy.ProcessStrategy(tradeState);

            result.StrategyAction.Should().Be(StrategyAction.NoAction,
                "price below SL means setup is stale for a Long");
        }

        [Fact]
        public void Long_PriceAboveTakeProfit_ReturnsNoAction()
        {
            // Long setup at 100k, TP at 100,750 (100k + 500*1.5)
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m);
            var (strategy, quoteHub, _) = CreateStrategy(setup);

            // Price surged above TP
            FillQuoteHub(quoteHub, 101_000m);

            var tradeState = new TradeState(false, 0m, 0m, 0m);
            var result = strategy.ProcessStrategy(tradeState);

            result.StrategyAction.Should().Be(StrategyAction.NoAction,
                "price above TP means setup is stale for a Long");
        }

        [Fact]
        public void Short_PriceAboveStopLoss_ReturnsNoAction()
        {
            // Short setup at 100k, SL at 100,750 (100k + 500*1.5)
            var setup = CreateSetup(TradeDirection.Short, 100_000m, 500m);
            var (strategy, quoteHub, _) = CreateStrategy(setup);

            // Price rose above SL
            FillQuoteHub(quoteHub, 101_000m);

            var tradeState = new TradeState(false, 0m, 0m, 0m);
            var result = strategy.ProcessStrategy(tradeState);

            result.StrategyAction.Should().Be(StrategyAction.NoAction,
                "price above SL means setup is stale for a Short");
        }

        [Fact]
        public void Short_PriceBelowTakeProfit_ReturnsNoAction()
        {
            // Short setup at 100k, TP at 99,250 (100k - 500*1.5)
            var setup = CreateSetup(TradeDirection.Short, 100_000m, 500m);
            var (strategy, quoteHub, _) = CreateStrategy(setup);

            // Price dropped below TP
            FillQuoteHub(quoteHub, 99_000m);

            var tradeState = new TradeState(false, 0m, 0m, 0m);
            var result = strategy.ProcessStrategy(tradeState);

            result.StrategyAction.Should().Be(StrategyAction.NoAction,
                "price below TP means setup is stale for a Short");
        }

        #endregion

        #region Valid Entry Signals

        [Fact]
        public void Long_PriceBetweenSlAndTp_ReturnsOpenTrade()
        {
            // Long setup at 100k, SL at 99,250, TP at 100,750
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m);
            var (strategy, quoteHub, _) = CreateStrategy(setup);

            // Price is within SL-TP range
            FillQuoteHub(quoteHub, 100_000m);

            var tradeState = new TradeState(false, 0m, 0m, 0m);
            var result = strategy.ProcessStrategy(tradeState);

            result.StrategyAction.Should().Be(StrategyAction.OpenTrade);
        }

        [Fact]
        public void Short_PriceBetweenTpAndSl_ReturnsOpenTrade()
        {
            // Short setup at 100k, SL at 100,750, TP at 99,250
            var setup = CreateSetup(TradeDirection.Short, 100_000m, 500m);
            var (strategy, quoteHub, _) = CreateStrategy(setup);

            // Price is within TP-SL range
            FillQuoteHub(quoteHub, 100_000m);

            var tradeState = new TradeState(false, 0m, 0m, 0m);
            var result = strategy.ProcessStrategy(tradeState);

            result.StrategyAction.Should().Be(StrategyAction.OpenTrade);
        }

        #endregion

        #region Insufficient Quotes

        [Fact]
        public void InsufficientQuotes_ReturnsNoAction()
        {
            var setup = CreateSetup(TradeDirection.Long);
            var (strategy, quoteHub, _) = CreateStrategy(setup);

            // Only add 10 quotes (need at least 15)
            FillQuoteHub(quoteHub, 100_000m, 10);

            var tradeState = new TradeState(false, 0m, 0m, 0m);
            var result = strategy.ProcessStrategy(tradeState);

            result.StrategyAction.Should().Be(StrategyAction.NoAction,
                "insufficient quotes should prevent any action");
        }

        #endregion

        #region Open Trade Exit Logic

        [Fact]
        public void TradeOpen_DelegatesToExitStrategy()
        {
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m);
            var (strategy, quoteHub, _) = CreateStrategy(setup);

            FillQuoteHub(quoteHub, 100_000m);

            var tradeState = new TradeState(true, 0.5m, 1.0m, 0m);
            var result = strategy.ProcessStrategy(tradeState);

            // When trade is open, strategy state should be WaitingForExit
            result.StrategyState.Should().Be(StrategyState.WaitingForExit);
        }

        #endregion

        #region SL/TP Rebase on Actual Entry

        [Fact]
        public void TradeOpen_RebasesSlTpToActualEntryPrice()
        {
            // Setup at 100k, but trade opens when price is at 100,300
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m);
            var originalRisk = setup.InitialRisk; // 750
            var (strategy, quoteHub, _) = CreateStrategy(setup);

            // Fill with price at 100,300 (different from setup entry of 100k)
            FillQuoteHub(quoteHub, 100_300m);

            var tradeState = new TradeState(true, 0m, 1.0m, 0m);
            strategy.ProcessStrategy(tradeState);

            // SL/TP should be recalculated from actual price (100,300), not setup (100,000)
            setup.EntryPrice.Should().Be(100_300m);
            setup.StopLoss.Should().Be(100_300m - originalRisk);
            setup.TakeProfit.Should().Be(100_300m + originalRisk);
        }

        [Fact]
        public void Short_TradeOpen_RebasesSlTpCorrectly()
        {
            // Short setup at 100k, trade opens at 99,800
            var setup = CreateSetup(TradeDirection.Short, 100_000m, 500m);
            var originalRisk = setup.InitialRisk; // 750
            var (strategy, quoteHub, _) = CreateStrategy(setup);

            FillQuoteHub(quoteHub, 99_800m);

            var tradeState = new TradeState(true, 0m, 1.0m, 0m);
            strategy.ProcessStrategy(tradeState);

            setup.EntryPrice.Should().Be(99_800m);
            setup.StopLoss.Should().Be(99_800m + originalRisk);
            setup.TakeProfit.Should().Be(99_800m - originalRisk);
        }

        [Fact]
        public void Rebase_OnlyHappensOnce_PerTrade()
        {
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m);
            var (strategy, quoteHub, _) = CreateStrategy(setup);

            FillQuoteHub(quoteHub, 100_300m);

            var tradeState = new TradeState(true, 0m, 1.0m, 0m);

            // First call rebases
            strategy.ProcessStrategy(tradeState);
            setup.EntryPrice.Should().Be(100_300m);

            // Add quote at different price and call again
            quoteHub.Add(new Quote
            {
                Timestamp = DateTime.Now,
                Open = 100_500m, High = 100_510m, Low = 100_490m,
                Close = 100_500m, Volume = 100
            });
            strategy.ProcessStrategy(tradeState);

            // Entry should NOT change to 100,500
            setup.EntryPrice.Should().Be(100_300m,
                "rebase should only happen once per trade, not every candle");
        }

        #endregion

        #region Setup Consumed (One Trade Per Setup)

        [Fact]
        public void SetupConsumed_NoReEntryAfterTradeCompletes()
        {
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m);
            var (strategy, quoteHub, tradingState) = CreateStrategy(setup);

            FillQuoteHub(quoteHub, 100_000m);

            var tradeState = new TradeState(false, 0m, 0m, 0m);

            // First call: enters trade
            var result1 = strategy.ProcessStrategy(tradeState);
            result1.StrategyAction.Should().Be(StrategyAction.OpenTrade);

            // Simulate trade completing (exit strategy records it)
            tradingState.RecordTradeComplete("TakeProfit", 500m);

            // Advance gap counter past MinGapCandles
            for (int i = 0; i < 10; i++)
                tradingState.OnNewCandle();

            // Second call with trade closed: setup is consumed, should NOT re-enter
            var result2 = strategy.ProcessStrategy(tradeState);
            result2.StrategyAction.Should().Be(StrategyAction.NoAction,
                "setup is consumed after one trade; algorithm must provide a fresh setup");
        }

        // Note: Gap/cooldown checks (CanTrade) are enforced by the algorithm
        // before the setup reaches the execution strategy. The algorithm sets
        // IsInPosition = true when firing a setup, so the execution strategy
        // does not duplicate that check.

        #endregion
    }
}
