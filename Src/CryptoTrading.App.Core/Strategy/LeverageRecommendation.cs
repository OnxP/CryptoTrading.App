namespace CryptoTrading.App.Core.Strategy
{
    public class LeverageRecommendation
    {
        public decimal CompositeScore { get; set; }
        public decimal RegimeScore { get; set; }
        public decimal SetupScore { get; set; }
        public decimal VolatilityScore { get; set; }
        public decimal ZoneScore { get; set; }
        public int RecommendedLeverage { get; set; }
        public int ActualLeverage { get; set; }
        public string ConfidenceTier { get; set; }
        public bool IsLeverageEnabled { get; set; }

        public override string ToString()
        {
            return $"Score: {CompositeScore:P0} ({ConfidenceTier}) | " +
                   $"Recommended: {RecommendedLeverage}x | Actual: {ActualLeverage}x | " +
                   $"[Regime={RegimeScore:P0} Setup={SetupScore:P0} Vol={VolatilityScore:P0} Zone={ZoneScore:P0}]";
        }
    }
}
