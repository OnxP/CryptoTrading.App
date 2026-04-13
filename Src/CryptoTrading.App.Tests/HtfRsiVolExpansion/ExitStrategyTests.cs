using CryptoTrading.App.Algorithm.HtfRsiVolExpansion;
using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core.Strategy;
using FluentAssertions;
using Skender.Stock.Indicators;
using Xunit;

namespace CryptoTrading.App.Tests.HtfRsiVolExpansion
{
    public class ExitStrategyTests
    {
        private HtfRsiVolExpansionSetup CreateSetup(
            TradeDirection direction,
            decimal entryPrice = 100_000m,
            decimal atr = 500m)
        {
            var risk = atr * 1.5m;
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
                HtfRsi = 70.0,
                VolExpansionRatio = 1.5,
                ProbabilityScore = 75,
                Quantity = 1.0m,
                Leverage = 5
            };
        }

        private (HtfRsiVolExpansionExitStrategy exit, QuoteHub<IQuote> quoteHub)
            CreateExitStrategy(HtfRsiVolExpansionSetup setup)
        {
            var tradingState = new HtfRsiTradingState(100_000m);
            tradingState.IsInPosition = true;
            var exit = new HtfRsiVolExpansionExitStrategy(setup, tradingState);
            var quoteHub = new QuoteHub<IQuote>(500);
            exit.SetQuotes(quoteHub);
            return (exit, quoteHub);
        }

        private void AddQuote(QuoteHub<IQuote> hub, DateTime time, decimal close,
            decimal high = 0, decimal low = 0)
        {
            if (high == 0) high = close + 10;
            if (low == 0) low = close - 10;
            hub.Add(new Quote
            {
                Timestamp = time,
                Open = close,
                High = high,
                Low = low,
                Close = close,
                Volume = 100
            });
        }

        private void FillInitialQuotes(QuoteHub<IQuote> hub, decimal price, int count = 20)
        {
            var baseTime = new DateTime(2025, 7, 1, 0, 0, 0);
            for (int i = 0; i < count; i++)
                AddQuote(hub, baseTime.AddMinutes(i), price);
        }

        #region Stop Loss

        [Fact]
        public void Long_StopLoss_TriggersWhenLowBreachesSL()
        {
            // Long at 100k, SL at 99,250
            var setup = CreateSetup(TradeDirection.Long);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Low drops below SL
            AddQuote(hub, DateTime.Now, 99_000m, high: 99_500m, low: 99_200m);

            var result = exit.GetNextExit(1.0m, 99_000m, 0m);

            result.ShouldTrade.Should().BeTrue();
            result.Price.Should().Be(setup.StopLoss);
        }

        [Fact]
        public void Short_StopLoss_TriggersWhenHighBreachesSL()
        {
            // Short at 100k, SL at 100,750
            var setup = CreateSetup(TradeDirection.Short);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // High rises above SL
            AddQuote(hub, DateTime.Now, 101_000m, high: 100_800m, low: 100_500m);

            var result = exit.GetNextExit(1.0m, 101_000m, 0m);

            result.ShouldTrade.Should().BeTrue();
            result.Price.Should().Be(setup.StopLoss);
        }

        #endregion

        #region Breakeven Stop

        [Fact]
        public void Long_Breakeven_MovesSlToEntryAt1R()
        {
            // Long at 100k, risk = 750 (500 * 1.5). Breakeven at 1R = 100,750
            var setup = CreateSetup(TradeDirection.Long);
            var originalSl = setup.StopLoss; // 99,250
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Price moves up to 1R profit (close at 100,750)
            AddQuote(hub, DateTime.Now, 100_750m, high: 100_800m, low: 100_700m);

            var result = exit.GetNextExit(1.0m, 100_750m, 0m);

            result.ShouldTrade.Should().BeFalse("breakeven moves SL but doesn't trigger exit");
            setup.StopLoss.Should().Be(setup.EntryPrice,
                "SL should move to entry price (breakeven) at 1.0R profit");
        }

        [Fact]
        public void Short_Breakeven_MovesSlToEntryAt1R()
        {
            // Short at 100k, risk = 750. Breakeven at 1R = 99,250
            var setup = CreateSetup(TradeDirection.Short);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Price drops to 1R profit (close at 99,250)
            AddQuote(hub, DateTime.Now, 99_250m, high: 99_300m, low: 99_200m);

            var result = exit.GetNextExit(1.0m, 99_250m, 0m);

            result.ShouldTrade.Should().BeFalse("breakeven moves SL but doesn't trigger exit");
            setup.StopLoss.Should().Be(setup.EntryPrice,
                "SL should move to entry price (breakeven) at 1.0R profit");
        }

        [Fact]
        public void Long_AfterBreakeven_StopLossTriggersAtEntry()
        {
            // Long at 100k, risk = 750
            var setup = CreateSetup(TradeDirection.Long);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Price reaches 1R to activate breakeven
            AddQuote(hub, DateTime.Now, 100_750m, high: 100_800m, low: 100_700m);
            exit.GetNextExit(1.0m, 100_750m, 0m);

            // Price reverses back to entry
            AddQuote(hub, DateTime.Now.AddMinutes(1), 100_000m, high: 100_100m, low: 99_990m);
            var result = exit.GetNextExit(1.0m, 100_000m, 0m);

            result.ShouldTrade.Should().BeTrue("SL is now at entry, price hit it");
            result.Price.Should().Be(100_000m, "exit at breakeven (entry price)");
        }

        #endregion

        #region Trailing Stop

        [Fact]
        public void Long_TrailingStop_ActivatesAt1_5R_AndTrails()
        {
            // Long at 100k, risk = 750. Trailing at 1.5R = 1,125 above entry = 101,125
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Price rises to 1.5R (101,125)
            AddQuote(hub, DateTime.Now, 101_200m, high: 101_200m, low: 101_100m);
            var result1 = exit.GetNextExit(1.0m, 101_200m, 0m);
            result1.ShouldTrade.Should().BeFalse("trailing just activated, not triggered yet");

            // Price continues up
            AddQuote(hub, DateTime.Now.AddMinutes(1), 101_500m, high: 101_500m, low: 101_400m);
            var result2 = exit.GetNextExit(1.0m, 101_500m, 0m);
            result2.ShouldTrade.Should().BeFalse("trailing follows price up");

            // Price reverses — trail is at highest (101,500) - ATR (500) = 101,000
            AddQuote(hub, DateTime.Now.AddMinutes(2), 100_900m, high: 101_000m, low: 100_900m);
            var result3 = exit.GetNextExit(1.0m, 100_900m, 0m);
            result3.ShouldTrade.Should().BeTrue("price dropped below trailing stop");
        }

        [Fact]
        public void Short_TrailingStop_ActivatesAt1_5R_AndTrails()
        {
            // Short at 100k, risk = 750. Trailing at 1.5R = 1,125 below entry = 98,875
            var setup = CreateSetup(TradeDirection.Short, 100_000m, 500m);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Price drops to 1.5R (98,875)
            AddQuote(hub, DateTime.Now, 98_800m, high: 98_900m, low: 98_800m);
            var result1 = exit.GetNextExit(1.0m, 98_800m, 0m);
            result1.ShouldTrade.Should().BeFalse("trailing just activated, not triggered yet");

            // Price continues down
            AddQuote(hub, DateTime.Now.AddMinutes(1), 98_500m, high: 98_600m, low: 98_500m);
            var result2 = exit.GetNextExit(1.0m, 98_500m, 0m);
            result2.ShouldTrade.Should().BeFalse("trailing follows price down");

            // Price reverses — trail is at lowest (98,500) + ATR (500) = 99,000
            AddQuote(hub, DateTime.Now.AddMinutes(2), 99_100m, high: 99_100m, low: 99_000m);
            var result3 = exit.GetNextExit(1.0m, 99_100m, 0m);
            result3.ShouldTrade.Should().BeTrue("price rose above trailing stop");
        }

        [Fact]
        public void Long_NoHardTakeProfit_PricePassesOldTpLevel()
        {
            // Long at 100k, old TP would have been at 100,750 (1R)
            // With no hard TP, price should pass through without exiting
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Price at old TP level — should NOT exit
            AddQuote(hub, DateTime.Now, 100_800m, high: 100_800m, low: 100_700m);
            var result = exit.GetNextExit(1.0m, 100_800m, 0m);

            result.ShouldTrade.Should().BeFalse(
                "no hard TP — trade should stay open and let trailing stop manage profit");
        }

        #endregion

        #region Time Stop

        [Fact]
        public void TimeStop_TriggersAfter240Bars()
        {
            // Setup with wide SL so it doesn't trigger
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 50_000m);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Simulate 240 bars (4 hours on 1M) within SL range
            var baseTime = DateTime.Now;
            for (int i = 0; i < 239; i++)
            {
                AddQuote(hub, baseTime.AddMinutes(i), 100_000m);
                var hold = exit.GetNextExit(1.0m, 100_000m, 0m);
                hold.ShouldTrade.Should().BeFalse($"bar {i + 1} should not trigger time stop");
            }

            // Bar 240 triggers time stop
            AddQuote(hub, baseTime.AddMinutes(239), 100_000m);
            var result = exit.GetNextExit(1.0m, 100_000m, 0m);

            result.ShouldTrade.Should().BeTrue("time stop should trigger at 240 bars");
        }

        #endregion

        #region No Exit When Within Range

        [Fact]
        public void NoExit_WhenPriceWithinRange()
        {
            var setup = CreateSetup(TradeDirection.Long);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Price stays within SL range, below breakeven activation
            AddQuote(hub, DateTime.Now, 100_200m, high: 100_300m, low: 100_100m);

            var result = exit.GetNextExit(1.0m, 100_200m, 0m);

            result.ShouldTrade.Should().BeFalse();
        }

        #endregion
    }
}
