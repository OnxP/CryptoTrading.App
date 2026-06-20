using System;
using System.Collections.Generic;

namespace CryptoTrading.App.Algorithm.DualRegime.Persistence
{
    public class DualRegimeStateSnapshot
    {
        public TradingStateSnapshot TradingState { get; set; }
        public AggregatorSnapshot Aggregator { get; set; }
        public SetupSnapshot ActiveSetup { get; set; }
        public int EvalCount { get; set; }
        public int[] Pred4HDist { get; set; }
        public int[] Pred1HDist { get; set; }
        public int ModelArtifactVersion { get; set; }
        public DateTime LastBarTime { get; set; }
    }

    public class TradingStateSnapshot
    {
        public decimal CurrentEquity { get; set; }
        public bool IsInPosition { get; set; }
        public int TotalTrades { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
    }

    public class AggregatorSnapshot
    {
        public List<BarSnapshot> Bars15M { get; set; } = new();
        public List<AggBarSnapshot> Bars1H { get; set; } = new();
        public List<AggBarSnapshot> Bars4H { get; set; } = new();
        public int Bar15MCount { get; set; }
        public int LastCompleted4HRegime { get; set; }
        public List<PhaseAccumulatorSnapshot> Phases1H { get; set; } = new();
        public List<PhaseAccumulatorSnapshot> Phases4H { get; set; } = new();
    }

    public class BarSnapshot
    {
        public DateTime Timestamp { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public double Ret { get; set; }
        public int Class15M { get; set; }
    }

    public class AggBarSnapshot
    {
        public DateTime Timestamp { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public double Ret { get; set; }
        public int ClassCombined { get; set; }
        public int Regime4H3Class { get; set; }
        public int[] SubClasses { get; set; }
        public double[] SubRets { get; set; }
    }

    public class PhaseAccumulatorSnapshot
    {
        public int GroupSize { get; set; }
        public int Offset { get; set; }
        public double Sigma { get; set; }
        public int Count { get; set; }
        public int LastClass { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public double RetSum { get; set; }
    }

    public class SetupSnapshot
    {
        public string StrategyType { get; set; }
        public string SignalType { get; set; }
        public string Direction { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal StopLoss { get; set; }
        public decimal TakeProfit { get; set; }
        public decimal Quantity { get; set; }
        public int Leverage { get; set; }
        public double StopPct { get; set; }
        public double TargetPct { get; set; }
        public DateTime EntryTime { get; set; }
        public decimal PeakPrice { get; set; }
        public double Mfe { get; set; }
        public double Mae { get; set; }
        public int BarsHeld { get; set; }
        public bool BeEngaged { get; set; }
        public bool TrailEngaged { get; set; }
    }
}
