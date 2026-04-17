using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.HtfRsiVolExpansion
{
    /// <summary>
    /// Shared mutable state between the algorithm and execution strategy.
    /// Tracks position state, trade gap, cooldown, and performance for
    /// the HTF RSI + LTF Vol Expansion strategy.
    /// </summary>
    public class HtfRsiTradingState
    {
        // Position tracking
        public bool IsInPosition { get; set; }
        public int CandlesSinceLastExit { get; set; } = 100;

        // Cooldown
        public int ConsecutiveLosses { get; set; }
        public int CooldownCandlesRemaining { get; set; }

        // Performance
        public int TotalTrades { get; set; }
        public int TotalWins { get; set; }
        public decimal CurrentEquity { get; set; }
        public decimal PeakEquity { get; set; }

        // Recent results for probability scorer (last 5 trades)
        private readonly List<bool> _recentResults = new List<bool>();

        // Parameters
        // 9 × 15M = 135 min — matches the observed minimum inter-trade gap in
        // the target backtest (all 435 consecutive gaps ≥ 135 min).
        public int MinGapCandles { get; set; } = 9;
        public int CooldownTriggerLosses { get; set; } = 3;
        public int CooldownDurationCandles { get; set; } = 16;

        public HtfRsiTradingState(decimal initialEquity)
        {
            CurrentEquity = initialEquity;
            PeakEquity = initialEquity;
        }

        /// <summary>
        /// Called on each new 15M candle to advance gap and cooldown counters.
        /// </summary>
        public void OnNewCandle()
        {
            if (!IsInPosition)
            {
                if (CandlesSinceLastExit < 10000)
                    CandlesSinceLastExit++;
                if (CooldownCandlesRemaining > 0)
                    CooldownCandlesRemaining--;
            }
        }

        /// <summary>
        /// Whether all non-indicator conditions allow a new trade.
        /// </summary>
        public bool CanTrade =>
            !IsInPosition
            && CandlesSinceLastExit >= MinGapCandles
            && CooldownCandlesRemaining <= 0;

        /// <summary>
        /// Called by the exit strategy when a trade completes.
        /// </summary>
        public void RecordTradeComplete(string exitReason, decimal pnlUsdt)
        {
            IsInPosition = false;
            CandlesSinceLastExit = 0;
            TotalTrades++;

            bool isWin = exitReason switch
            {
                "TakeProfit" => true,
                "TrailingStop" => true,
                "StopLoss" => false,
                "TimeStop" => pnlUsdt > 0,
                _ => pnlUsdt > 0
            };

            CurrentEquity += pnlUsdt;
            if (CurrentEquity > PeakEquity)
                PeakEquity = CurrentEquity;

            if (isWin)
            {
                TotalWins++;
                ConsecutiveLosses = 0;
            }
            else
            {
                ConsecutiveLosses++;
                if (ConsecutiveLosses >= CooldownTriggerLosses)
                    CooldownCandlesRemaining = CooldownDurationCandles;
            }

            _recentResults.Add(isWin);
            if (_recentResults.Count > 5)
                _recentResults.RemoveAt(0);
        }

        /// <summary>
        /// Win rate of the last 5 trades (0.0 - 1.0). Returns 0.6 if fewer than 5 trades.
        /// </summary>
        public double RecentWinRate =>
            _recentResults.Count >= 5
                ? _recentResults.Count(r => r) / (double)_recentResults.Count
                : 0.6;

        public string GetStatus()
        {
            return $"Equity:{CurrentEquity:F2} Peak:{PeakEquity:F2} " +
                   $"W:{TotalWins} L:{TotalTrades - TotalWins} " +
                   $"ConsecL:{ConsecutiveLosses} CD:{CooldownCandlesRemaining} " +
                   $"Gap:{CandlesSinceLastExit} CanTrade:{CanTrade}";
        }
    }
}
