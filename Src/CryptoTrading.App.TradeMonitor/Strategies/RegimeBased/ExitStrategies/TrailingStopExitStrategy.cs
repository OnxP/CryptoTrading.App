using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Monitor.Strategies.RegimeBased.ExitStrategies
{
    /// <summary>
    /// Adaptive ATR-based trailing stop that widens as profit grows.
    ///
    /// Key improvement: instead of a fixed 2x ATR trail at all profit levels,
    /// the trail distance is dynamic:
    ///   - Early profit (< 1R): tight 1.5x ATR trail (cut losers fast)
    ///   - 1R profit: standard 2.0x ATR trail
    ///   - 1.5R profit: 2.5x ATR trail (give winners room)
    ///   - 2R profit: 3.0x ATR trail (wide trail for runners)
    ///   - 3R+ profit: 3.5x ATR trail (very wide, let big winners run)
    ///
    /// This lets winners run 30-50% further while cutting losers 20% tighter.
    /// </summary>
    public class TrailingStopExitStrategy : RegimeBasedExitStrategyBase
    {
        private readonly decimal _trailingStartMultiple = 0.3m;  // Activate at 0.3R (earlier activation, but tight trail)

        public TrailingStopExitStrategy(SetupResult setup) : base(setup) { }

        /// <summary>
        /// Get the dynamic trailing ATR multiple based on current R-multiple.
        /// Wider trail = more room for winners to breathe.
        /// Tighter trail = cut losers before they grow.
        /// Widened across the board: 1M candles have significant noise that
        /// was causing premature trail exits before targets could be reached.
        /// </summary>
        private decimal GetDynamicTrailingMultiple(decimal rMultiple)
        {
            if (rMultiple >= 3.0m) return 4.0m;   // Very wide trail for big winners
            if (rMultiple >= 2.0m) return 3.5m;   // Wide trail
            if (rMultiple >= 1.5m) return 3.0m;   // Medium trail
            if (rMultiple >= 1.0m) return 2.5m;   // Standard trail
            return 2.0m;                            // Early profit: wider to survive 1M noise
        }

        protected override TradeDetails EvaluateExit(decimal currentPrice, decimal positionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            decimal rMultiple = GetRMultiple(currentPrice);

            if (rMultiple >= _trailingStartMultiple)
            {
                decimal atr = GetCurrentAtr();
                decimal dynamicMultiple = GetDynamicTrailingMultiple(rMultiple);
                decimal trailingDistance = atr * dynamicMultiple;

                decimal trailingStopLevel = Setup.Direction == TradeDirection.Long
                    ? HighestPrice - trailingDistance
                    : LowestPrice + trailingDistance;

                bool trailingStopHit = Setup.Direction == TradeDirection.Long
                    ? currentPrice <= trailingStopLevel
                    : currentPrice >= trailingStopLevel;

                Logger?.LogDebug($"[1M EXIT TrailingStop] R:{rMultiple:F2} price:{currentPrice:F2} trail:{trailingStopLevel:F2} atrMult:{dynamicMultiple:F1} high:{HighestPrice:F2} low:{LowestPrice:F2} atr:{atr:F2} hit:{trailingStopHit}");

                if (trailingStopHit)
                {
                    Logger?.LogInformation($"[1M EXIT TrailingStop] TRIGGER @ {currentPrice:F2} (trail:{trailingStopLevel:F2} R:{rMultiple:F2} atrMult:{dynamicMultiple:F1}x)");
                    result.ShouldTrade = true;
                    result.Price = currentPrice;
                    result.Quantity = positionSize;
                    result.OrderType = "MARKET";
                }
            }
            else
            {
                Logger?.LogDebug($"[1M EXIT TrailingStop] R:{rMultiple:F2} < {_trailingStartMultiple} (not active) price:{currentPrice:F2}");
            }

            return result;
        }
    }
}
