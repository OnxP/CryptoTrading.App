using Binance;
using CryptoTrading.App.Core.Strategy;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// Result of setup evaluation on 15M timeframe
    /// </summary>
    public class SetupResult
    {
        public SetupType SetupType { get; set; }
        public TradeDirection Direction { get; set; }
        public bool IsValid { get; set; }

        // Entry zone
        public decimal EntryZoneHigh { get; set; }
        public decimal EntryZoneLow { get; set; }

        // Risk management
        public decimal StopLoss { get; set; }
        public decimal TakeProfit { get; set; }
        public decimal RiskRewardRatio { get; set; }

        // Confidence and explanation
        public decimal Confidence { get; set; }
        public string Reasoning { get; set; } = string.Empty;

        // Recommended strategies for execution layer
        public EntryStrategyType RecommendedEntryStrategy { get; set; }
        public ExitStrategyType RecommendedExitStrategy { get; set; }

        // Supply/demand zone context for 1M entry strategies
        public SupplyDemandZone NearestZone { get; set; }
        public bool IsZoneTrade { get; set; }
    }

    /// <summary>
    /// Extended strategy result that includes the setup details
    /// </summary>
    public class RegimeBasedStrategyResult : StrategyResult
    {
        public SetupResult Setup { get; set; }
    }
}