using System;
using CryptoTrading.App.Core.Strategy;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// Calculates recommended leverage based on a composite probability score
    /// derived from regime confidence, setup quality, and market conditions.
    ///
    /// The calculator outputs a probability score (0.0 - 1.0) and maps it to
    /// a leverage tier. When leverage is disabled (default), it always returns 1x.
    ///
    /// Probability Score Composition:
    ///   - Regime confidence (40%): How clearly the market regime is identified
    ///   - Setup confidence (30%): Quality of the trade setup (momentum, zones, R:R)
    ///   - Volatility factor (15%): Lower volatility = safer for leverage
    ///   - Zone confluence (15%): Presence of supply/demand zone alignment
    ///
    /// Leverage Tiers (when enabled):
    ///   Score >= 0.85 => MaxLeverage (e.g. 3x)
    ///   Score >= 0.70 => MidLeverage (e.g. 2x)
    ///   Score >= 0.50 => MinLeverage (e.g. 1x)
    ///   Score  < 0.50 => No trade (leverage = 0)
    /// </summary>
    public class LeverageProbabilityCalculator
    {
        // Weights for composite score
        private const decimal RegimeWeight = 0.40m;
        private const decimal SetupWeight = 0.30m;
        private const decimal VolatilityWeight = 0.15m;
        private const decimal ZoneWeight = 0.15m;

        // Leverage tier thresholds
        public decimal HighConfidenceThreshold { get; set; } = 0.85m;
        public decimal MediumConfidenceThreshold { get; set; } = 0.70m;
        public decimal MinimumConfidenceThreshold { get; set; } = 0.55m;  // Raised from 0.50 to filter out marginal setups

        // Leverage multipliers per tier
        public int MaxLeverage { get; set; } = 3;
        public int MidLeverage { get; set; } = 2;
        public int MinLeverage { get; set; } = 1;

        /// <summary>
        /// When false (default), the calculator always returns leverage = 1
        /// regardless of the probability score. The score is still calculated
        /// for monitoring purposes.
        /// </summary>
        public bool IsLeverageEnabled { get; set; } = false;

        /// <summary>
        /// Calculate the composite probability score from all contributing factors.
        /// </summary>
        /// <param name="regimeConfidence">Regime detection confidence (0.0 - 1.0)</param>
        /// <param name="setupConfidence">Setup evaluation confidence (0.0 - 1.0)</param>
        /// <param name="volatilityRegime">Current volatility regime</param>
        /// <param name="atrPercentile">ATR percentile (0 - 100)</param>
        /// <param name="isZoneTrade">Whether the trade aligns with a supply/demand zone</param>
        /// <param name="riskRewardRatio">Risk-reward ratio of the trade</param>
        /// <returns>Composite probability score and recommended leverage</returns>
        public LeverageRecommendation Calculate(
            decimal regimeConfidence,
            decimal setupConfidence,
            VolatilityRegime volatilityRegime,
            decimal atrPercentile,
            bool isZoneTrade,
            decimal riskRewardRatio)
        {
            // 1. Regime confidence score (already 0-1)
            var regimeScore = Math.Clamp(regimeConfidence, 0, 1);

            // 2. Setup confidence score (already 0-1)
            var setupScore = Math.Clamp(setupConfidence, 0, 1);

            // 3. Volatility factor: moderate volatility is ideal for leverage
            //    Low vol = moderate confidence (predictable but small moves)
            //    Normal vol = high confidence (good moves, manageable risk)
            //    High vol = low confidence (large moves but unpredictable)
            var volatilityScore = CalculateVolatilityScore(volatilityRegime, atrPercentile);

            // 4. Zone confluence: zone trades are higher probability
            var zoneScore = 0m;
            if (isZoneTrade)
            {
                zoneScore = 0.8m;
                // Better R:R at zones adds more confidence
                if (riskRewardRatio >= 2.5m)
                    zoneScore = 1.0m;
                else if (riskRewardRatio >= 2.0m)
                    zoneScore = 0.9m;
            }
            else
            {
                // Non-zone trades: base score from R:R quality
                if (riskRewardRatio >= 3.0m)
                    zoneScore = 0.5m;
                else if (riskRewardRatio >= 2.0m)
                    zoneScore = 0.3m;
            }

            // Composite weighted score
            var compositeScore = Math.Clamp(
                regimeScore * RegimeWeight +
                setupScore * SetupWeight +
                volatilityScore * VolatilityWeight +
                zoneScore * ZoneWeight,
                0m, 1m);

            // Map score to leverage tier
            int recommendedLeverage;
            string tier;

            if (compositeScore >= HighConfidenceThreshold)
            {
                recommendedLeverage = MaxLeverage;
                tier = "High";
            }
            else if (compositeScore >= MediumConfidenceThreshold)
            {
                recommendedLeverage = MidLeverage;
                tier = "Medium";
            }
            else if (compositeScore >= MinimumConfidenceThreshold)
            {
                recommendedLeverage = MinLeverage;
                tier = "Low";
            }
            else
            {
                recommendedLeverage = 0; // Below minimum - skip trade
                tier = "None";
            }

            // Override to 1x when leverage is disabled
            var actualLeverage = IsLeverageEnabled
                ? recommendedLeverage
                : (recommendedLeverage > 0 ? 1 : 0);

            return new LeverageRecommendation
            {
                CompositeScore = compositeScore,
                RegimeScore = regimeScore,
                SetupScore = setupScore,
                VolatilityScore = volatilityScore,
                ZoneScore = zoneScore,
                RecommendedLeverage = recommendedLeverage,
                ActualLeverage = actualLeverage,
                ConfidenceTier = tier,
                IsLeverageEnabled = IsLeverageEnabled
            };
        }

        private decimal CalculateVolatilityScore(VolatilityRegime regime, decimal atrPercentile)
        {
            switch (regime)
            {
                case VolatilityRegime.Normal:
                    // Normal volatility is ideal - moderate to high score
                    return 0.8m;

                case VolatilityRegime.Low:
                    // Low volatility - predictable but leverage amplifies small moves
                    return 0.6m;

                case VolatilityRegime.High:
                    // High volatility - dangerous for leverage
                    // Scale down further for extreme volatility
                    if (atrPercentile > 90)
                        return 0.1m;  // Extreme - avoid leverage entirely
                    if (atrPercentile > 80)
                        return 0.25m; // Very high - minimal leverage
                    return 0.4m;      // High but manageable

                default:
                    return 0.5m;
            }
        }
    }

    /// <summary>
    /// Result of the leverage probability calculation.
    /// Contains the composite score, component scores, and leverage recommendation.
    /// </summary>
    public class LeverageRecommendation
    {
        /// <summary>Weighted composite of all factors (0.0 - 1.0)</summary>
        public decimal CompositeScore { get; set; }

        /// <summary>Regime detection confidence component</summary>
        public decimal RegimeScore { get; set; }

        /// <summary>Setup quality component</summary>
        public decimal SetupScore { get; set; }

        /// <summary>Volatility suitability component</summary>
        public decimal VolatilityScore { get; set; }

        /// <summary>Zone confluence component</summary>
        public decimal ZoneScore { get; set; }

        /// <summary>Leverage that would be used if enabled</summary>
        public int RecommendedLeverage { get; set; }

        /// <summary>Leverage actually used (1x when leverage is disabled)</summary>
        public int ActualLeverage { get; set; }

        /// <summary>Confidence tier: None, Low, Medium, High</summary>
        public string ConfidenceTier { get; set; }

        /// <summary>Whether leverage was enabled for this calculation</summary>
        public bool IsLeverageEnabled { get; set; }

        public override string ToString()
        {
            return $"Score: {CompositeScore:P0} ({ConfidenceTier}) | " +
                   $"Recommended: {RecommendedLeverage}x | Actual: {ActualLeverage}x | " +
                   $"[Regime={RegimeScore:P0} Setup={SetupScore:P0} Vol={VolatilityScore:P0} Zone={ZoneScore:P0}]";
        }
    }
}
