using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using System;

namespace CryptoTrading.App.Monitor.Strategies.RegimeBased.ExitStrategies
{
    /// <summary>
    /// Hybrid fixed-target + trailing exit strategy for mean reversion setups.
    ///
    /// Improvement over old strategy:
    /// - Old: Exit at 65% progress toward TP when momentum exhausted (left 35% on table)
    /// - New: Once at 50% progress, activate a tight trailing stop that lets the
    ///   trade run if momentum continues, while protecting profit if it reverses.
    ///
    /// The trailing stop uses 2.5x ATR initially, widening to 3.0x ATR as profit grows.
    /// Wider trail prevents premature exits from 1M noise while still protecting profit.
    /// If momentum exhausts near the target, exits cleanly rather than waiting for a reversal.
    /// </summary>
    public class FixedTargetExitStrategy : RegimeBasedExitStrategyBase
    {
        private bool _trailingActivated;

        public FixedTargetExitStrategy(SetupResult setup) : base(setup) { }

        public override void ResetForNewTrade()
        {
            base.ResetForNewTrade();
            _trailingActivated = false;
        }

        protected override TradeDetails EvaluateExit(decimal currentPrice, decimal positionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            decimal totalTargetDistance = Math.Abs(Setup.TakeProfit - EntryPrice);
            if (totalTargetDistance <= 0) return result;

            decimal distanceToTarget = Setup.Direction == TradeDirection.Long
                ? Setup.TakeProfit - currentPrice
                : currentPrice - Setup.TakeProfit;
            decimal progressToTarget = 1 - (distanceToTarget / totalTargetDistance);

            decimal rMultiple = GetRMultiple(currentPrice);
            decimal atr = GetCurrentAtr();

            Logger?.LogDebug($"[1M EXIT FixedTarget] progress:{progressToTarget:P0} R:{rMultiple:F2} price:{currentPrice:F2} tp:{Setup.TakeProfit:F2} trailActive:{_trailingActivated}");

            // Activate trailing once we reach 50% of target
            if (progressToTarget >= 0.50m)
                _trailingActivated = true;

            // Early full exit: momentum exhausted at 80%+ progress (very close to target)
            if (progressToTarget >= 0.80m && IsMomentumExhausted())
            {
                Logger?.LogInformation($"[1M EXIT FixedTarget] TRIGGER @ {currentPrice:F2} (progress:{progressToTarget:P0} momentum exhausted near target)");
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize;
                result.OrderType = "MARKET";
                return result;
            }

            // Trailing stop after 50% progress: protect profit while allowing continuation
            if (_trailingActivated && atr > 0)
            {
                // Trail gets wider as we approach target (more room for the final push)
                // Widened from 1.5/2.0 to 2.5/3.0 — 1M candles have significant noise
                decimal trailMultiple = progressToTarget >= 0.75m ? 3.0m : 2.5m;
                decimal trailingDistance = atr * trailMultiple;

                decimal trailingStopLevel = Setup.Direction == TradeDirection.Long
                    ? HighestPrice - trailingDistance
                    : LowestPrice + trailingDistance;

                bool trailingStopHit = Setup.Direction == TradeDirection.Long
                    ? currentPrice <= trailingStopLevel
                    : currentPrice >= trailingStopLevel;

                if (trailingStopHit)
                {
                    Logger?.LogInformation($"[1M EXIT FixedTarget] TRAILING STOP @ {currentPrice:F2} (trail:{trailingStopLevel:F2} progress:{progressToTarget:P0} atrMult:{trailMultiple:F1})");
                    result.ShouldTrade = true;
                    result.Price = currentPrice;
                    result.Quantity = positionSize;
                    result.OrderType = "MARKET";
                    return result;
                }
            }

            return result;
        }
    }
}
