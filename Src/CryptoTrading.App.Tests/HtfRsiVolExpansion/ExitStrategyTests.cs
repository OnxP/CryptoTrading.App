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

        #region Take Profit

        [Fact]
        public void Long_TakeProfit_TriggersWhenHighBreachesTP()
        {
            // Long at 100k, TP at 100,750
            var setup = CreateSetup(TradeDirection.Long);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // High rises above TP
            AddQuote(hub, DateTime.Now, 100_800m, high: 100_800m, low: 100_500m);

            var result = exit.GetNextExit(1.0m, 100_800m, 0m);

            result.ShouldTrade.Should().BeTrue();
            result.Price.Should().Be(setup.TakeProfit);
        }

        [Fact]
        public void Short_TakeProfit_TriggersWhenLowBreachesTP()
        {
            // Short at 100k, TP at 99,250
            var setup = CreateSetup(TradeDirection.Short);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Low drops below TP
            AddQuote(hub, DateTime.Now, 99_000m, high: 99_500m, low: 99_200m);

            var result = exit.GetNextExit(1.0m, 99_000m, 0m);

            result.ShouldTrade.Should().BeTrue();
            result.Price.Should().Be(setup.TakeProfit);
        }

        #endregion

        #region Time Stop

        [Fact]
        public void TimeStop_TriggersAfter240Bars()
        {
            // Setup with wide SL/TP so they don't trigger
            var setup = CreateSetup(TradeDirection.Long, 100_000m, 50_000m);
            var (exit, hub) = CreateExitStrategy(setup);
            FillInitialQuotes(hub, 100_000m);

            // Simulate 240 bars (4 hours on 1M) within SL/TP range
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

            // Price stays within SL-TP range
            AddQuote(hub, DateTime.Now, 100_200m, high: 100_300m, low: 100_100m);

            var result = exit.GetNextExit(1.0m, 100_200m, 0m);

            result.ShouldTrade.Should().BeFalse();
        }

        #endregion
    }
}
