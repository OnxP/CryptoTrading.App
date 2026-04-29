using CryptoTrading.App.Algorithm.HtfRsiVolExpansion;
using CryptoTrading.App.Algorithm.RegimeBased;
using FluentAssertions;
using Skender.Stock.Indicators;
using System;
using Xunit;

namespace CryptoTrading.App.Tests.HtfRsiVolExpansion
{
    /// <summary>
    /// Pins the realised-PnL accounting for both exit strategies:
    /// PnL = priceDelta * quantity. Quantity already encodes leverage at
    /// sizing time (notional = equity * leverage), so multiplying by leverage
    /// again would inflate every exit by the leverage factor. Earlier code
    /// did exactly that - the resulting +1234% headline backtest result
    /// was a 5x artefact of that double-count.
    /// </summary>
    public class ExitStrategyPnlTests
    {
        private static HtfRsiVolExpansionSetup CreateSetup(
            TradeDirection direction,
            decimal entryPrice = 100_000m,
            decimal atr = 500m,
            decimal quantity = 1.0m,
            int leverage = 5,
            int probabilityScore = 30) // 30 -> 1R TP, easy to drive
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
                HtfRsi = direction == TradeDirection.Long ? 72.0 : 32.0,
                VolExpansionRatio = 1.5,
                ProbabilityScore = probabilityScore,
                Quantity = quantity,
                Leverage = leverage
            };
        }

        // Fixed seed time. Last seed quote sits at 00:14 so the test's trigger
        // quote at 00:15 lands on a 15M boundary - both exit strategies gate
        // their decisions to closeTime.Minute % 15 == 0.
        private static readonly DateTime _seedStart = new DateTime(2025, 7, 1, 0, 0, 0);

        private static void SeedQuotes(QuoteHub<IQuote> hub, decimal price, int count = 15)
        {
            for (int i = 0; i < count; i++)
            {
                hub.Add(new Quote
                {
                    Timestamp = _seedStart.AddMinutes(i),
                    Open = price,
                    High = price + 10m,
                    Low = price - 10m,
                    Close = price,
                    Volume = 100
                });
            }
        }

        private static void AddTriggerQuote(
            QuoteHub<IQuote> hub, decimal close, decimal high, decimal low)
        {
            // Lands at 00:15 - a 15M boundary, satisfying the exit strategies'
            // gate so SL/TP/trail decisions actually evaluate.
            hub.Add(new Quote
            {
                Timestamp = _seedStart.AddMinutes(15),
                Open = close,
                High = high,
                Low = low,
                Close = close,
                Volume = 100
            });
        }

        #region SimpleExitStrategy - production path

        [Fact]
        public void SimpleExit_Long_StopLoss_PnlIsPriceDeltaTimesQuantity_NoLeverageMultiplier()
        {
            // entry 100,000 | ATR 500 | risk 750 | qty 1 | leverage 5
            // SL at 99,250. Expected PnL = (99,250 - 100,000) * 1 = -750.
            // BUGGY behaviour would have been -750 * 5 = -3,750.
            var setup = CreateSetup(TradeDirection.Long, quantity: 1m, leverage: 5);
            var state = new HtfRsiTradingState(initialEquity: 100_000m) { IsInPosition = true };
            var hub = new QuoteHub<IQuote>(500);
            var exit = new SimpleExitStrategy(setup, state);
            exit.SetQuotes(hub);
            SeedQuotes(hub, 100_000m);

            // Drive a low that breaches the SL.
            AddTriggerQuote(hub, close: 99_000m, high: 99_500m, low: 99_200m);
            var result = exit.GetNextExit(setup.Quantity, 99_000m, 0m);

            result.ShouldTrade.Should().BeTrue();
            state.LastExitPnl.Should().Be(-750m);
            state.CurrentEquity.Should().Be(100_000m - 750m);
        }

        [Fact]
        public void SimpleExit_Short_StopLoss_PnlIsPriceDeltaTimesQuantity_NoLeverageMultiplier()
        {
            // entry 100,000 short | SL 100,750 | qty 1 | leverage 5
            // Expected PnL = (100,000 - 100,750) * 1 = -750.
            var setup = CreateSetup(TradeDirection.Short, quantity: 1m, leverage: 5);
            var state = new HtfRsiTradingState(initialEquity: 100_000m) { IsInPosition = true };
            var hub = new QuoteHub<IQuote>(500);
            var exit = new SimpleExitStrategy(setup, state);
            exit.SetQuotes(hub);
            SeedQuotes(hub, 100_000m);

            // Drive a high that breaches the short SL.
            AddTriggerQuote(hub, close: 101_000m, high: 100_900m, low: 100_500m);
            var result = exit.GetNextExit(setup.Quantity, 101_000m, 0m);

            result.ShouldTrade.Should().BeTrue();
            state.LastExitPnl.Should().Be(-750m);
            state.CurrentEquity.Should().Be(100_000m - 750m);
        }

        [Fact]
        public void SimpleExit_Long_TakeProfit_PnlScalesLinearlyWithQuantity_NotLeverage()
        {
            // entry 100,000 | TP 100,750 | qty 2 | leverage 10
            // Expected PnL = (100,750 - 100,000) * 2 = +1,500.
            // BUGGY: 1,500 * 10 = 15,000.
            var setup = CreateSetup(
                TradeDirection.Long,
                quantity: 2m,
                leverage: 10,
                probabilityScore: 30); // -> 1R TP at entry+750
            var state = new HtfRsiTradingState(initialEquity: 50_000m) { IsInPosition = true };
            var hub = new QuoteHub<IQuote>(500);
            var exit = new SimpleExitStrategy(setup, state);
            exit.SetQuotes(hub);
            SeedQuotes(hub, 100_000m);

            // Drive a high above TP.
            AddTriggerQuote(hub, close: 100_800m, high: 100_900m, low: 100_700m);
            var result = exit.GetNextExit(setup.Quantity, 100_800m, 0m);

            result.ShouldTrade.Should().BeTrue();
            state.LastExitPnl.Should().Be(1_500m);
        }

        [Fact]
        public void SimpleExit_PnlIsIndependentOfLeverageField_AtFixedQuantity()
        {
            // Same setup at qty=1, run twice with leverage 1 and 25.
            // The Leverage field is metadata only - it must NOT affect PnL.
            decimal RunOnce(int leverage)
            {
                var setup = CreateSetup(TradeDirection.Long, quantity: 1m, leverage: leverage);
                var state = new HtfRsiTradingState(initialEquity: 100_000m) { IsInPosition = true };
                var hub = new QuoteHub<IQuote>(500);
                var exit = new SimpleExitStrategy(setup, state);
                exit.SetQuotes(hub);
                SeedQuotes(hub, 100_000m);

                AddTriggerQuote(hub, close: 99_000m, high: 99_500m, low: 99_200m);
                exit.GetNextExit(setup.Quantity, 99_000m, 0m);
                return state.LastExitPnl;
            }

            var pnl1x = RunOnce(1);
            var pnl25x = RunOnce(25);

            pnl1x.Should().Be(-750m);
            pnl25x.Should().Be(-750m, "leverage is encoded in qty at sizing - exit pnl must not multiply by it");
        }

        #endregion

        #region HtfRsiVolExpansionExitStrategy - same accounting

        [Fact]
        public void HtfRsiExit_Long_StopLoss_PnlIsPriceDeltaTimesQuantity_NoLeverageMultiplier()
        {
            // entry 100,000 | SL 99,250 | qty 1 | leverage 5
            var setup = CreateSetup(TradeDirection.Long, quantity: 1m, leverage: 5);
            var state = new HtfRsiTradingState(initialEquity: 100_000m) { IsInPosition = true };
            var hub = new QuoteHub<IQuote>(500);
            var exit = new HtfRsiVolExpansionExitStrategy(setup, state);
            exit.SetQuotes(hub);
            SeedQuotes(hub, 100_000m);

            AddTriggerQuote(hub, close: 99_000m, high: 99_500m, low: 99_200m);
            var result = exit.GetNextExit(setup.Quantity, 99_000m, 0m);

            result.ShouldTrade.Should().BeTrue();
            state.LastExitPnl.Should().Be(-750m);
        }

        [Fact]
        public void HtfRsiExit_Short_StopLoss_PnlIsPriceDeltaTimesQuantity_NoLeverageMultiplier()
        {
            var setup = CreateSetup(TradeDirection.Short, quantity: 1m, leverage: 5);
            var state = new HtfRsiTradingState(initialEquity: 100_000m) { IsInPosition = true };
            var hub = new QuoteHub<IQuote>(500);
            var exit = new HtfRsiVolExpansionExitStrategy(setup, state);
            exit.SetQuotes(hub);
            SeedQuotes(hub, 100_000m);

            AddTriggerQuote(hub, close: 101_000m, high: 100_900m, low: 100_500m);
            var result = exit.GetNextExit(setup.Quantity, 101_000m, 0m);

            result.ShouldTrade.Should().BeTrue();
            state.LastExitPnl.Should().Be(-750m);
        }

        #endregion
    }
}
