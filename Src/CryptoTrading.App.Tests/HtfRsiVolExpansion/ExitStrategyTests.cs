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
            decimal atr = 500m,
            int probabilityScore = 75)
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
                ProbabilityScore = probabilityScore,
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
            var setup = CreateSetup(TradeDirection.Long);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            AddQuote(hub, DateTime.Now, 99_000m, high: 99_500m, low: 99_200m);

            var result = exit.GetNextExit(1.0m, 99_000m, 0m);

            result.ShouldTrade.Should().BeTrue();
            result.Price.Should().Be(setup.StopLoss);
        }

        [Fact]
        public void Short_StopLoss_TriggersWhenHighBreachesSL()
        {
            var setup = CreateSetup(TradeDirection.Short);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            AddQuote(hub, DateTime.Now, 101_000m, high: 100_800m, low: 100_500m);

            var result = exit.GetNextExit(1.0m, 101_000m, 0m);

            result.ShouldTrade.Should().BeTrue();
            result.Price.Should().Be(setup.StopLoss);
        }

        #endregion

        #region Dynamic Take Profit (Score-Based)

        [Theory]
        [InlineData(85, 0)]      // Score 80+: no hard TP
        [InlineData(80, 0)]      // Score exactly 80: no hard TP
        [InlineData(75, 2.0)]    // Score 60-79: 2R
        [InlineData(60, 2.0)]    // Score exactly 60: 2R
        [InlineData(55, 1.5)]    // Score 40-59: 1.5R
        [InlineData(40, 1.5)]    // Score exactly 40: 1.5R
        [InlineData(30, 1.0)]    // Score <40: 1.0R
        [InlineData(0, 1.0)]     // Score 0: 1.0R
        public void GetTakeProfitMultiplier_ReturnsCorrectR(int score, decimal expectedR)
        {
            HtfRsiVolExpansionExitStrategy.GetTakeProfitMultiplier(score)
                .Should().Be(expectedR);
        }

        [Fact]
        public void Long_Score75_TakeProfitAt2R()
        {
            // Score 75 → 2.0R TP. Risk = 750. TP at entry + 1,500 = 101,500
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m, probabilityScore: 75);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Price reaches 2R
            AddQuote(hub, DateTime.Now, 101_600m, high: 101_600m, low: 101_400m);
            var result = exit.GetNextExit(1.0m, 101_600m, 0m);

            result.ShouldTrade.Should().BeTrue();
            result.Price.Should().Be(100_000m + 750m * 2.0m, "TP at 2R for score 75");
        }

        [Fact]
        public void Long_Score75_NoExitBelow2R()
        {
            // Score 75 → 2.0R TP = 101,500. Price at 1.5R (101,125) should NOT exit
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m, probabilityScore: 75);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            AddQuote(hub, DateTime.Now, 101_100m, high: 101_100m, low: 101_000m);
            var result = exit.GetNextExit(1.0m, 101_100m, 0m);

            result.ShouldTrade.Should().BeFalse("price below 2R TP for score 75");
        }

        [Fact]
        public void Short_Score50_TakeProfitAt1_5R()
        {
            // Score 50 → 1.5R TP. Risk = 750. TP at entry - 1,125 = 98,875
            var setup = CreateSetup(TradeDirection.Short, 100_000m, 500m, probabilityScore: 50);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            AddQuote(hub, DateTime.Now, 98_800m, high: 98_900m, low: 98_800m);
            var result = exit.GetNextExit(1.0m, 98_800m, 0m);

            result.ShouldTrade.Should().BeTrue();
            result.Price.Should().Be(100_000m - 750m * 1.5m, "TP at 1.5R for score 50");
        }

        [Fact]
        public void Long_Score30_TakeProfitAt1R()
        {
            // Score 30 → 1.0R TP. Risk = 750. TP at entry + 750 = 100,750
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m, probabilityScore: 30);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            AddQuote(hub, DateTime.Now, 100_800m, high: 100_800m, low: 100_700m);
            var result = exit.GetNextExit(1.0m, 100_800m, 0m);

            result.ShouldTrade.Should().BeTrue();
            result.Price.Should().Be(100_000m + 750m * 1.0m, "TP at 1.0R for score 30");
        }

        [Fact]
        public void Long_Score85_NoHardTP_TrailingStopManages()
        {
            // Score 85 → no hard TP. Price at 3R should NOT trigger TP exit
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m, probabilityScore: 85);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Price reaches 3R — no TP should fire
            AddQuote(hub, DateTime.Now, 102_300m, high: 102_300m, low: 102_200m);
            var result = exit.GetNextExit(1.0m, 102_300m, 0m);

            result.ShouldTrade.Should().BeFalse(
                "score 85 has no hard TP — trailing stop manages profit");
        }

        #endregion

        #region Trailing Stop Activation at 1R

        [Fact]
        public void Long_TrailingActivatesAt1R_LocksInPartialProfit()
        {
            // Score 85 (no hard TP). Trailing at 1R locks in ~0.33R.
            // Entry 100k, ATR 500, Risk 750.
            // At 1R profit: close >= 100,750. Highest = 100,800.
            // Trail = 100,800 - 500 = 100,300 → locks in 300 (0.4R).
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m, probabilityScore: 85);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Price moves up to 1R — trailing activates
            AddQuote(hub, DateTime.Now, 100_750m, high: 100_800m, low: 100_700m);
            var result1 = exit.GetNextExit(1.0m, 100_750m, 0m);
            result1.ShouldTrade.Should().BeFalse("trailing just activated, not triggered yet");

            // Price reverses — trail at 100,800 - 500 = 100,300
            AddQuote(hub, DateTime.Now.AddMinutes(1), 100_200m, high: 100_300m, low: 100_200m);
            var result2 = exit.GetNextExit(1.0m, 100_200m, 0m);
            result2.ShouldTrade.Should().BeTrue("price dropped below trailing stop at 100,300");
            result2.Price.Should().Be(100_300m, "trail exit at highest - ATR");
        }

        [Fact]
        public void Long_TrailingAt1R_SurvivesModerateRetracement()
        {
            // Price goes to 1R, then pulls back slightly — should survive
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m, probabilityScore: 85);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Activate trailing at 1R (close 100,750, high 100,800)
            AddQuote(hub, DateTime.Now, 100_750m, high: 100_800m, low: 100_700m);
            exit.GetNextExit(1.0m, 100_750m, 0m);

            // Trail = 100,800 - 500 = 100,300. Pull back to 100,400 — should survive
            AddQuote(hub, DateTime.Now.AddMinutes(1), 100_400m, high: 100_500m, low: 100_350m);
            var result = exit.GetNextExit(1.0m, 100_400m, 0m);
            result.ShouldTrade.Should().BeFalse(
                "price still above trailing stop at 100,300 — trade survives the bounce");
        }

        #endregion

        #region Trailing Stop

        [Fact]
        public void Long_TrailingStop_ActivatesAt1R_AndTrails()
        {
            // Score 85 → no hard TP, trailing can activate at 1R
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 500m, probabilityScore: 85);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Price rises to 1R+ (100,800)
            AddQuote(hub, DateTime.Now, 100_800m, high: 100_800m, low: 100_700m);
            var result1 = exit.GetNextExit(1.0m, 100_800m, 0m);
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
        public void Short_TrailingStop_ActivatesAt1R_AndTrails()
        {
            var setup = CreateSetup(TradeDirection.Short, 100_000m, 500m, probabilityScore: 85);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Price drops to 1R+ (99,200)
            AddQuote(hub, DateTime.Now, 99_200m, high: 99_300m, low: 99_200m);
            var result1 = exit.GetNextExit(1.0m, 99_200m, 0m);
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

        #endregion

        #region Time Stop

        [Fact]
        public void TimeStop_TriggersAfter240Bars()
        {
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 50_000m, probabilityScore: 85);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            var baseTime = DateTime.Now;
            for (int i = 0; i < 239; i++)
            {
                AddQuote(hub, baseTime.AddMinutes(i), 100_000m);
                var hold = exit.GetNextExit(1.0m, 100_000m, 0m);
                hold.ShouldTrade.Should().BeFalse($"bar {i + 1} should not trigger time stop");
            }

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

            AddQuote(hub, DateTime.Now, 100_200m, high: 100_300m, low: 100_100m);

            var result = exit.GetNextExit(1.0m, 100_200m, 0m);

            result.ShouldTrade.Should().BeFalse();
        }

        #endregion
    }
}
