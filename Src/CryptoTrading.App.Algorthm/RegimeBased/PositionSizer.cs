using System;
using CryptoTrading.App.Core.Strategy;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// Volatility-adjusted position sizing.
    ///
    /// Instead of fixed 0.1 BTC per trade, sizes based on:
    /// 1. Risk per trade: fixed % of equity (default 1%)
    /// 2. Stop distance: tighter stop = bigger position (more reward per risk unit)
    /// 3. Anti-martingale multiplier: reduces after losing streaks
    /// 4. Regime multiplier: sizes up in clear trends, down in ranges
    ///
    /// Formula: quantity = (equity * riskPct * streakMult * regimeMult) / stopDistance
    /// Clamped to [minQuantity, maxEquityPct * equity / entryPrice]
    /// </summary>
    public static class PositionSizer
    {
        // Default risk: 1% of equity per trade
        private const decimal DefaultRiskPercentage = 0.01m;
        // Minimum BTC order size
        private const decimal MinQuantity = 0.001m;
        // Maximum percentage of equity in a single position
        private const decimal MaxEquityPercentage = 0.25m;

        /// <summary>
        /// Calculate position size based on risk parameters.
        /// </summary>
        /// <param name="equity">Current account equity in quote currency (USDT)</param>
        /// <param name="entryPrice">Expected entry price</param>
        /// <param name="stopLoss">Stop loss price</param>
        /// <param name="streakMultiplier">Anti-martingale multiplier from TradePerformanceTracker</param>
        /// <param name="regimeMultiplier">Regime-based scaling multiplier</param>
        /// <param name="riskPercentage">Risk per trade as decimal (0.01 = 1%)</param>
        /// <returns>Position size in base currency (BTC)</returns>
        public static decimal Calculate(
            decimal equity,
            decimal entryPrice,
            decimal stopLoss,
            decimal streakMultiplier = 1.0m,
            decimal regimeMultiplier = 1.0m,
            decimal riskPercentage = DefaultRiskPercentage)
        {
            if (equity <= 0 || entryPrice <= 0) return MinQuantity;

            decimal stopDistance = Math.Abs(entryPrice - stopLoss);
            if (stopDistance <= 0) return MinQuantity;

            // Base risk amount in USDT
            decimal riskAmount = equity * riskPercentage;

            // Apply multipliers
            riskAmount *= Math.Clamp(streakMultiplier, 0.25m, 1.5m);
            riskAmount *= Math.Clamp(regimeMultiplier, 0.3m, 1.5m);

            // Convert to quantity: how much BTC can we buy such that
            // if price moves by stopDistance, we lose exactly riskAmount?
            decimal quantity = riskAmount / stopDistance;

            // Clamp: minimum order and maximum equity exposure
            decimal maxQuantity = (equity * MaxEquityPercentage) / entryPrice;
            quantity = Math.Clamp(quantity, MinQuantity, maxQuantity);

            // Round to 3 decimal places (BTC precision)
            return Math.Round(quantity, 5);
        }

        /// <summary>
        /// Get regime-based position scaling multiplier.
        /// Clear trends get larger positions; uncertain/ranging markets get smaller.
        /// </summary>
        public static decimal GetRegimeMultiplier(
            MarketRegime regime,
            VolatilityRegime volatility,
            decimal confidence,
            decimal trendStrength)
        {
            decimal multiplier = 1.0m;

            // Strong trending regimes get a boost
            if (regime == MarketRegime.BullMarket || regime == MarketRegime.BearMarket)
            {
                if (trendStrength > 0.3m && confidence > 0.7m)
                    multiplier = 1.3m;    // 30% larger in clear strong trends
                else if (trendStrength > 0.15m)
                    multiplier = 1.15m;   // 15% larger in moderate trends
            }

            // Ranging markets get reduced size
            if (regime == MarketRegime.RangingMarket)
                multiplier = 0.7m;        // 30% smaller in ranges

            // High volatility reduces across the board
            if (volatility == VolatilityRegime.High)
                multiplier *= 0.7m;       // Additional 30% reduction in high vol

            return Math.Clamp(multiplier, 0.3m, 1.5m);
        }
    }
}
