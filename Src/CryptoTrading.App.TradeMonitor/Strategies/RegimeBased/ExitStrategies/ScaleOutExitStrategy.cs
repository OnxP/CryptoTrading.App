using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using System;

namespace CryptoTrading.App.Monitor.Strategies.RegimeBased.ExitStrategies
{
    /// <summary>
    /// Multi-target partial exit strategy with breakeven stop.
    ///
    /// Exit plan:
    ///   - At 1R: Exit 33% of position, move SL to breakeven (+0.1% buffer for fees)
    ///   - At 2R: Exit another 33%
    ///   - At 3R or momentum exhausted: Exit remaining position
    ///
    /// After first partial, the worst case for remaining 67% is breakeven (not a loss).
    /// This eliminates the painful "round-trip" where a trade goes from profit back to loss.
    ///
    /// All exits use MARKET orders for reliable fills (was LIMIT at 1R which often didn't fill).
    /// </summary>
    public class ScaleOutExitStrategy : RegimeBasedExitStrategyBase
    {
        private int _partialExitsTaken;
        private decimal _originalPositionSize;
        private bool _stopMovedToBreakeven;

        public ScaleOutExitStrategy(SetupResult setup) : base(setup) { }

        public override void ResetForNewTrade()
        {
            base.ResetForNewTrade();
            _partialExitsTaken = 0;
            _originalPositionSize = 0;
            _stopMovedToBreakeven = false;
        }

        /// <summary>
        /// Move the stop loss to breakeven (+0.1% buffer to cover fees).
        /// Only happens once, after the first partial exit.
        /// </summary>
        private void MoveStopToBreakeven()
        {
            if (_stopMovedToBreakeven || EntryPrice <= 0) return;

            decimal buffer = EntryPrice * 0.001m; // 0.1% above entry for fees
            Setup.StopLoss = Setup.Direction == TradeDirection.Long
                ? EntryPrice + buffer
                : EntryPrice - buffer;
            _stopMovedToBreakeven = true;
            Logger?.LogInformation($"[1M EXIT ScaleOut] SL moved to breakeven: {Setup.StopLoss:F2} (entry:{EntryPrice:F2})");
        }

        protected override TradeDetails EvaluateExit(decimal currentPrice, decimal positionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            decimal rMultiple = GetRMultiple(currentPrice);

            // Track original size for consistent partial sizing
            if (_originalPositionSize == 0 && positionSize > 0)
                _originalPositionSize = positionSize;

            Logger?.LogDebug($"[1M EXIT ScaleOut] R:{rMultiple:F2} price:{currentPrice:F2} pos:{positionSize} partials:{_partialExitsTaken} beSL:{_stopMovedToBreakeven}");

            // Target 1: 1R — exit 33%, move SL to breakeven
            if (_partialExitsTaken == 0 && rMultiple >= 1.0m)
            {
                _partialExitsTaken = 1;
                MoveStopToBreakeven();

                var exitQty = Math.Round(_originalPositionSize * 0.33m, 5);
                Logger?.LogInformation($"[1M EXIT ScaleOut] PARTIAL 1/3 @ 1R ({rMultiple:F2}) price:{currentPrice:F2} qty:{exitQty}");
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = exitQty;
                result.OrderType = "MARKET";  // MARKET for reliable fills (was LIMIT)
                return result;
            }

            // Target 2: 2R — exit another 33%
            if (_partialExitsTaken == 1 && rMultiple >= 2.0m)
            {
                _partialExitsTaken = 2;

                var exitQty = Math.Round(_originalPositionSize * 0.33m, 5);
                // Don't exit more than we have
                exitQty = Math.Min(exitQty, positionSize);
                Logger?.LogInformation($"[1M EXIT ScaleOut] PARTIAL 2/3 @ 2R ({rMultiple:F2}) price:{currentPrice:F2} qty:{exitQty}");
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = exitQty;
                result.OrderType = "MARKET";
                return result;
            }

            // Target 3: 3R or momentum exhausted — exit remaining
            if (_partialExitsTaken == 2 && (rMultiple >= 3.0m || IsMomentumExhausted()))
            {
                _partialExitsTaken = 3;
                var reason = rMultiple >= 3.0m ? "3R target" : "momentum exhausted";
                Logger?.LogInformation($"[1M EXIT ScaleOut] FULL EXIT ({reason}) @ R:{rMultiple:F2} price:{currentPrice:F2} qty:{positionSize}");
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize; // exit everything remaining
                result.OrderType = "MARKET";
                return result;
            }

            return result;
        }
    }
}
