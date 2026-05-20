using CryptoTrading.App.Core.Strategy;
using System;

namespace CryptoTrading.App.Monitor.Strategies.HtfRsiVolExpansion
{
    /// <summary>
    /// Setup data for the HTF RSI + LTF Vol Expansion strategy.
    /// Created by the algorithm when entry conditions are met.
    /// </summary>
    public class HtfRsiVolExpansionSetup
    {
        public TradeDirection Direction { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal StopLoss { get; set; }
        public decimal TakeProfit { get; set; }
        public decimal AtrAtEntry { get; set; }
        public decimal InitialRisk { get; set; }  // 1.5 × ATR

        // Indicator values at entry (for logging)
        public double HtfRsi { get; set; }
        public double VolExpansionRatio { get; set; }
        public double Rsi15M { get; set; }
        public int ProbabilityScore { get; set; }

        // Timing
        public DateTime EntryTime { get; set; }

        // Position sizing
        public decimal Quantity { get; set; }
        public int Leverage { get; set; } = 5;

        // When true, EntryPrice/StopLoss/TakeProfit are the caller's final
        // values and must NOT be rebased by the execution strategy on the
        // first 15M bar after fill. Set by the BbGuide deferred-entry path
        // in HtfRsiVolExpansionEntryStrategy, which pins these fields to
        // the 1M fill price at alignment rather than the 15M signal close.
        public bool EntryPriceFinal { get; set; }
    }

    /// <summary>
    /// Strategy result that carries the setup data for this strategy.
    /// Extends StrategyResult (IStrategyResult) to integrate with the existing
    /// RequestTracker / TradeMonitor pipeline.
    /// </summary>
    public class HtfRsiVolExpansionStrategyResult : StrategyResult
    {
        public HtfRsiVolExpansionSetup Setup { get; set; }
    }
}
