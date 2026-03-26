using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased.ExitStrategies
{
    /// <summary>
    /// Exits stagnant trades that aren't moving, preventing capital being tied up.
    ///
    /// Improvement over old strategy:
    /// - Old: 20 bars, exits if move < 0.5% (killed winners before they developed)
    /// - New: 60 bars (1 hour on 1M), exits only if move < 0.1% AND volatility is contracting
    ///
    /// This gives trades much more time to develop while still cutting truly dead positions.
    /// </summary>
    public class TimeBasedExitStrategy : RegimeBasedExitStrategyBase
    {
        private readonly int _timeStopBars = 60;                // 60 min (was 20 — too aggressive)
        private readonly decimal _stagnationThreshold = 0.1m;   // 0.1% (was 0.5% — too wide, killed valid small moves)

        public TimeBasedExitStrategy(SetupResult setup) : base(setup) { }

        protected override TradeDetails EvaluateExit(decimal currentPrice, decimal positionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            decimal pnlPercent = EntryPrice != 0
                ? (Setup.Direction == TradeDirection.Long
                    ? (currentPrice - EntryPrice) / EntryPrice * 100
                    : (EntryPrice - currentPrice) / EntryPrice * 100)
                : 0;

            bool tradeStagnant = Math.Abs(pnlPercent) < _stagnationThreshold;

            // Additional check: only time-exit if volatility is contracting
            // (if vol is expanding, a move is building — give it more time)
            bool volContracting = IsVolatilityContracting();

            Logger?.LogDebug($"[1M EXIT TimeBased] bars:{BarsHeld}/{_timeStopBars} pnl:{pnlPercent:F3}% stagnant:{tradeStagnant} volContracting:{volContracting}");

            if (BarsHeld >= _timeStopBars && tradeStagnant && volContracting)
            {
                Logger?.LogInformation($"[1M EXIT TimeBased] TRIGGER @ {currentPrice:F2} (bars:{BarsHeld} pnl:{pnlPercent:F3}% stagnant+contracting)");
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize;
                result.OrderType = "MARKET";
            }

            return result;
        }

        /// <summary>
        /// Check if volatility is contracting (recent ATR smaller than prior ATR).
        /// If volatility is expanding, a move may be building — don't exit prematurely.
        /// </summary>
        private bool IsVolatilityContracting()
        {
            try
            {
                var atrValues = QuoteHub.Quotes.ToAtr(14);
                if (atrValues == null || atrValues.Count < 20) return true; // Default to allow exit if insufficient data

                var recentAtr = 0m;
                var priorAtr = 0m;
                var count = atrValues.Count;

                // Average of last 5 ATR values vs prior 5
                int recentCount = 0, priorCount = 0;
                for (int i = count - 1; i >= count - 5 && i >= 0; i--)
                {
                    if (atrValues[i].Atr.HasValue) { recentAtr += (decimal)atrValues[i].Atr.Value; recentCount++; }
                }
                for (int i = count - 6; i >= count - 10 && i >= 0; i--)
                {
                    if (atrValues[i].Atr.HasValue) { priorAtr += (decimal)atrValues[i].Atr.Value; priorCount++; }
                }

                if (recentCount == 0 || priorCount == 0) return true;
                return (recentAtr / recentCount) <= (priorAtr / priorCount);
            }
            catch
            {
                return true; // Default to allow exit on error
            }
        }
    }
}
