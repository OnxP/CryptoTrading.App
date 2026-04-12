using Binance;
using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core.Strategy;
using System;

namespace CryptoTrading.App.Algorithm.HtfRsiVolExpansion
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
