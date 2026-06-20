using System.Collections.Generic;

namespace CryptoTrading.App.Algorithm.DualRegime
{
    public sealed class DualRegimeParams
    {
        // ── Trend strategy stops (P90 MAE per signal type) ──
        public Dictionary<string, double> TrendStops { get; set; } = new()
        {
            ["bull_bull"] = 0.0126,
            ["bull_neutral"] = 0.0166,
            ["bear_bear"] = 0.0122,
            ["bear_neutral"] = 0.0135,
        };

        // ── Trend target ──
        public double TargetVolMultiplier { get; set; } = 2.5;
        public double TargetMinPct { get; set; } = 0.012;
        public double TargetMaxPct { get; set; } = 0.040;

        // ── Trend trailing stop ──
        public double TrendBeTrigger { get; set; } = 0.007;
        public double TrendBeOffset { get; set; } = 0.003;
        public double TrendTrailTrigger { get; set; } = 0.015;
        public double TrendTrailLockFraction { get; set; } = 0.5;

        // ── Chop strategy ──
        public double ChopStopMult { get; set; } = 1.2;
        public double ChopStopMin { get; set; } = 0.002;
        public double ChopStopMax { get; set; } = 0.012;
        public double ChopBeTrigger { get; set; } = 0.0020;
        public double ChopBeOffset { get; set; } = 0.0025;
        public double ChopLockFraction { get; set; } = 0.90;

        // ── Shared ──
        public int MaxHoldBars { get; set; } = 8;
        public double CostPerSide { get; set; } = 0.0006;
        public double Slippage { get; set; } = 0.0004;
        public double TrendCapitalRisk { get; set; } = 0.01;
        public double ChopCapitalRisk { get; set; } = 0.005;
        public double EvThreshold { get; set; } = 0.003;
        // Leverage > 1 enables futures-mode USDT margin checking in the broker.
        // Position sizing is risk-based (not leverage-scaled), so this only
        // affects the balance gate.
        public int Leverage { get; set; } = 2;
    }
}
