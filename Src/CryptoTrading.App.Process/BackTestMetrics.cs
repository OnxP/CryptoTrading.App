using System;
using System.Collections.Generic;

namespace CryptoTrading.App.Process
{
    public class BacktestMetrics
    {
        // Equity
        public double StartEquityBtc { get; set; }
        public double EndEquityBtc { get; set; }
        public double NetProfitBtc => EndEquityBtc - StartEquityBtc;
        public double NetProfitPct => StartEquityBtc != 0 ? (NetProfitBtc / StartEquityBtc) * 100.0 : 0;

        // Trades
        public int TotalTrades { get; set; }
        public int Winners { get; set; }
        public int Losers { get; set; }
        public double WinRatePct => TotalTrades > 0 ? (double)Winners / TotalTrades * 100.0 : 0;

        // P&L stats
        public double GrossProfitBtc { get; set; }
        public double GrossLossBtc { get; set; } // negative
        public double ProfitFactor => Math.Abs(GrossLossBtc) < 1e-12 ? double.NaN : GrossProfitBtc / Math.Abs(GrossLossBtc);
        public double AvgTradePnlBtc { get; set; }
        public double AvgWinBtc { get; set; }
        public double AvgLossBtc { get; set; } // negative
        public double PayoffRatio => Math.Abs(AvgLossBtc) < 1e-12 ? double.NaN : AvgWinBtc / Math.Abs(AvgLossBtc);
        public double ExpectancyBtc { get; set; } // per trade
        public double ExpectancyPct { get; set; } // per trade %

        // Risk
        public double MaxDrawdownBtc { get; set; }
        public double MaxDrawdownPct { get; set; }

        // Streaks
        public int MaxConsecutiveWins { get; set; }
        public int MaxConsecutiveLosses { get; set; }

        // Time
        public TimeSpan AvgTradeDuration { get; set; }
        public double TradesPerDay { get; set; }

        // Optional
        public double SharpePerTrade { get; set; } // mean(ret)/stdev(ret), using per-trade pct returns
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        // Equity curve (CloseDate, EquityBtc)
        public List<(DateTime Time, double EquityBtc)> EquityCurve { get; } = new();
    }
}
