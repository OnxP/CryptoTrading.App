using System;
using CryptoTrading.App.Core.Strategy;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// Symbol-specific order constraints used by PositionSizer.
    /// Populated from the exchange symbol info (e.g. Binance Symbol.Quantity).
    /// </summary>
    public class SymbolConstraints
    {
        /// <summary>Minimum order quantity (e.g. 0.00001 BTC)</summary>
        public decimal MinQuantity { get; set; }

        /// <summary>Maximum order quantity</summary>
        public decimal MaxQuantity { get; set; }

        /// <summary>Quantity step/increment (e.g. 0.00001)</summary>
        public decimal StepSize { get; set; }

        /// <summary>Minimum order value in quote currency (e.g. 10 USDT)</summary>
        public decimal MinNotional { get; set; }

        /// <summary>Price tick size (e.g. 0.01)</summary>
        public decimal TickSize { get; set; }

        /// <summary>
        /// Default fallback for when symbol info isn't available.
        /// Uses conservative BTC defaults.
        /// </summary>
        public static SymbolConstraints Default => new SymbolConstraints
        {
            MinQuantity = 0.00001m,
            MaxQuantity = 9000m,
            StepSize = 0.00001m,
            MinNotional = 10m,
            TickSize = 0.01m
        };

        /// <summary>
        /// Round quantity down to the nearest valid step size.
        /// </summary>
        public decimal RoundToStepSize(decimal quantity)
        {
            if (StepSize <= 0) return quantity;
            return Math.Floor(quantity / StepSize) * StepSize;
        }
    }

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
    /// Clamped to [symbol.MinQuantity, maxEquityPct * equity / entryPrice]
    /// Rounded to symbol's step size.
    /// </summary>
    public static class PositionSizer
    {
        // Default risk: 1% of equity per trade
        private const decimal DefaultRiskPercentage = 0.01m;
        // Maximum percentage of equity in a single position
        private const decimal MaxEquityPercentage = 0.25m;

        /// <summary>
        /// Calculate position size based on risk parameters and symbol constraints.
        /// </summary>
        /// <param name="equity">Current account equity in quote currency (USDT)</param>
        /// <param name="entryPrice">Expected entry price</param>
        /// <param name="stopLoss">Stop loss price</param>
        /// <param name="symbolConstraints">Symbol-specific order constraints (min qty, step size, min notional)</param>
        /// <param name="streakMultiplier">Anti-martingale multiplier from TradePerformanceTracker</param>
        /// <param name="regimeMultiplier">Regime-based scaling multiplier</param>
        /// <param name="riskPercentage">Risk per trade as decimal (0.01 = 1%)</param>
        /// <returns>Position size in base currency, rounded to symbol's step size</returns>
        public static decimal Calculate(
            decimal equity,
            decimal entryPrice,
            decimal stopLoss,
            SymbolConstraints symbolConstraints = null,
            decimal streakMultiplier = 1.0m,
            decimal regimeMultiplier = 1.0m,
            decimal riskPercentage = DefaultRiskPercentage)
        {
            var constraints = symbolConstraints ?? SymbolConstraints.Default;

            if (equity <= 0 || entryPrice <= 0) return constraints.MinQuantity;

            decimal stopDistance = Math.Abs(entryPrice - stopLoss);
            if (stopDistance <= 0) return constraints.MinQuantity;

            // Base risk amount in USDT
            decimal riskAmount = equity * riskPercentage;

            // Apply multipliers
            riskAmount *= Math.Clamp(streakMultiplier, 0.25m, 1.5m);
            riskAmount *= Math.Clamp(regimeMultiplier, 0.3m, 1.5m);

            // Convert to quantity: how much can we buy such that
            // if price moves by stopDistance, we lose exactly riskAmount?
            decimal quantity = riskAmount / stopDistance;

            // Clamp: symbol minimum and maximum equity exposure
            decimal maxQuantity = (equity * MaxEquityPercentage) / entryPrice;
            if (constraints.MaxQuantity > 0)
                maxQuantity = Math.Min(maxQuantity, constraints.MaxQuantity);

            // If equity is too small for even the minimum order at our risk budget,
            // use the minimum order size (accepts higher risk % on this trade)
            if (maxQuantity < constraints.MinQuantity)
                return constraints.MinQuantity;

            quantity = Math.Max(quantity, constraints.MinQuantity);
            quantity = Math.Min(quantity, maxQuantity);

            // Round down to symbol's step size
            quantity = constraints.RoundToStepSize(quantity);

            // Ensure minimum notional is met (quantity * price >= minNotional)
            if (constraints.MinNotional > 0 && quantity * entryPrice < constraints.MinNotional)
            {
                // Increase to meet min notional
                quantity = Math.Ceiling(constraints.MinNotional / entryPrice / constraints.StepSize) * constraints.StepSize;
            }

            // Final clamp after adjustments
            quantity = Math.Max(quantity, constraints.MinQuantity);

            return quantity;
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
