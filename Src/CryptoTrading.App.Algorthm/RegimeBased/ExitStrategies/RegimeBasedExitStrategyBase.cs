using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased.ExitStrategies
{
    /// <summary>
    /// Abstract base class for all regime-based exit strategies.
    /// Provides shared position tracking, stop loss/take profit checks, and indicator helpers.
    /// Concrete subclasses implement specific exit logic.
    /// </summary>
    public abstract class RegimeBasedExitStrategyBase : IExitStrategy
    {
        protected QuoteHub<IQuote> QuoteHub;
        public SetupResult Setup { get; protected set; }
        protected ILogger Logger;

        public void SetLogger(ILogger logger) => Logger = logger;

        // Position tracking
        protected decimal HighestPrice;
        protected decimal LowestPrice;
        protected int BarsHeld;
        protected decimal EntryPrice;
        protected bool Initialized;

        protected RegimeBasedExitStrategyBase(SetupResult setup)
        {
            Setup = setup;
        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            QuoteHub = quoteHub;
        }

        /// <summary>
        /// Updates the setup with fresh SL/TP levels from a newer 15M setup.
        /// Called when a same-direction setup arrives while a trade is open.
        /// </summary>
        public void UpdateSetup(SetupResult newSetup)
        {
            if (newSetup == null) return;
            Logger?.LogInformation(
                $"[1M EXIT] Updating SL/TP: SL {Setup.StopLoss:F2}->{newSetup.StopLoss:F2}, " +
                $"TP {Setup.TakeProfit:F2}->{newSetup.TakeProfit:F2}");
            Setup = newSetup;
        }

        public void InitializePosition(decimal entryPrice)
        {
            EntryPrice = entryPrice;
            HighestPrice = entryPrice;
            LowestPrice = entryPrice;
            BarsHeld = 0;
            Initialized = true;
        }

        public TradeDetails GetNextExit(decimal currentPositionSize, decimal close, decimal profit)
        {
            var result = new TradeDetails { ShouldTrade = false };

            if (QuoteHub?.Quotes == null || Setup == null || currentPositionSize == 0)
                return result;

            // Auto-initialize if not done
            if (!Initialized)
            {
                EntryPrice = close;
                HighestPrice = close;
                LowestPrice = close;
                Initialized = true;
            }

            // Update tracking
            if (close > HighestPrice) HighestPrice = close;
            if (close < LowestPrice) LowestPrice = close;
            BarsHeld++;

            // Check hard stop — SL is algorithm-only, so the exit order is a new
            // market order at the current price, not at the theoretical SL level.
            if (CheckStopLoss(close))
            {
                Logger?.LogInformation($"[1M EXIT] STOP LOSS HIT @ {close:F2} (stop:{Setup.StopLoss:F2}) bars:{BarsHeld}");
                result.ShouldTrade = true;
                result.Price = close;
                result.Quantity = currentPositionSize;
                result.OrderType = "MARKET";
                return result;
            }

            // Check take profit — fill at actual market price, not the theoretical TP level.
            if (CheckTakeProfit(close))
            {
                Logger?.LogInformation($"[1M EXIT] TAKE PROFIT HIT @ {close:F2} (tp:{Setup.TakeProfit:F2}) bars:{BarsHeld}");
                result.ShouldTrade = true;
                result.Price = close;
                result.Quantity = currentPositionSize;
                result.OrderType = "MARKET";
                return result;
            }

            // Delegate to strategy-specific exit logic
            return EvaluateExit(close, currentPositionSize);
        }

        /// <summary>
        /// Evaluate strategy-specific exit conditions.
        /// Called after stop loss and take profit checks have passed.
        /// Implemented by each concrete exit strategy.
        /// </summary>
        protected abstract TradeDetails EvaluateExit(decimal currentPrice, decimal positionSize);

        /// <summary>
        /// Creates the correct exit strategy subclass based on the setup's recommended exit type.
        /// </summary>
        public static RegimeBasedExitStrategyBase Create(SetupResult setup)
        {
            return setup.RecommendedExitStrategy switch
            {
                ExitStrategyType.FixedTarget => new FixedTargetExitStrategy(setup),
                ExitStrategyType.TrailingStop => new TrailingStopExitStrategy(setup),
                ExitStrategyType.StructureBreak => new StructureBreakExitStrategy(setup),
                ExitStrategyType.TimeBasedExit => new TimeBasedExitStrategy(setup),
                ExitStrategyType.ScaleOut => new ScaleOutExitStrategy(setup),
                _ => new TrailingStopExitStrategy(setup)
            };
        }

        #region Shared Helpers

        private bool CheckStopLoss(decimal currentPrice)
        {
            return Setup.Direction == TradeDirection.Long
                ? currentPrice <= Setup.StopLoss
                : currentPrice >= Setup.StopLoss;
        }

        private bool CheckTakeProfit(decimal currentPrice)
        {
            return Setup.Direction == TradeDirection.Long
                ? currentPrice >= Setup.TakeProfit
                : currentPrice <= Setup.TakeProfit;
        }

        protected bool IsMomentumExhausted()
        {
            var rsi = QuoteHub.Quotes.ToRsi(14).LastOrDefault()?.Rsi ?? 50;

            if (Setup.Direction == TradeDirection.Long)
                return rsi > 70 && IsFallingRsi();
            else
                return rsi < 30 && IsRisingRsi();
        }

        protected bool IsFallingRsi()
        {
            var rsiValues = QuoteHub.Quotes.ToRsi(14).TakeLast(10).ToList();
            if (rsiValues.Count < 10) return false;

            var recent = rsiValues.TakeLast(5).Average(r => r.Rsi ?? 50);
            var prior = rsiValues.Take(5).Average(r => r.Rsi ?? 50);

            return recent < prior - 5;
        }

        protected bool IsRisingRsi()
        {
            var rsiValues = QuoteHub.Quotes.ToRsi(14).TakeLast(10).ToList();
            if (rsiValues.Count < 10) return false;

            var recent = rsiValues.TakeLast(5).Average(r => r.Rsi ?? 50);
            var prior = rsiValues.Take(5).Average(r => r.Rsi ?? 50);

            return recent > prior + 5;
        }

        protected decimal GetCurrentAtr()
        {
            var atr = QuoteHub.Quotes.ToAtr(14).LastOrDefault()?.Atr;
            return (decimal)(atr ?? 0);
        }

        /// <summary>
        /// Calculates the current R-multiple (profit relative to initial risk).
        /// </summary>
        protected decimal GetRMultiple(decimal currentPrice)
        {
            decimal riskDistance = Math.Abs(EntryPrice - Setup.StopLoss);
            decimal currentProfit = Setup.Direction == TradeDirection.Long
                ? currentPrice - EntryPrice
                : EntryPrice - currentPrice;

            return riskDistance > 0 ? currentProfit / riskDistance : 0;
        }

        #endregion
    }
}
