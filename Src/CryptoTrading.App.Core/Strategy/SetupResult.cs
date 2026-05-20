namespace CryptoTrading.App.Core.Strategy
{
    public class SetupResult
    {
        public SetupType SetupType { get; set; }
        public TradeDirection Direction { get; set; }
        public bool IsValid { get; set; }

        public decimal EntryZoneHigh { get; set; }
        public decimal EntryZoneLow { get; set; }

        public decimal StopLoss { get; set; }
        public decimal TakeProfit { get; set; }
        public decimal RiskRewardRatio { get; set; }

        public decimal Confidence { get; set; }
        public string Reasoning { get; set; } = string.Empty;

        public EntryStrategyType RecommendedEntryStrategy { get; set; }
        public ExitStrategyType RecommendedExitStrategy { get; set; }

        public SupplyDemandZone NearestZone { get; set; }
        public bool IsZoneTrade { get; set; }
    }

    public class RegimeBasedStrategyResult : StrategyResult
    {
        public SetupResult Setup { get; set; }
        public LeverageRecommendation LeverageRecommendation { get; set; }
    }
}
