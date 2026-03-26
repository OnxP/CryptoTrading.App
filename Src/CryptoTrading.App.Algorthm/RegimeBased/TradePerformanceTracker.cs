using System;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// Tracks trade performance to enable anti-martingale position sizing
    /// and drawdown circuit breakers.
    ///
    /// Anti-martingale: reduce size after consecutive losses, increase after wins.
    /// Circuit breaker: stop trading when daily loss exceeds 3% or peak drawdown exceeds 10%.
    /// </summary>
    public class TradePerformanceTracker
    {
        private int _consecutiveWins;
        private int _consecutiveLosses;
        private decimal _peakEquity;
        private decimal _currentEquity;
        private decimal _dayStartEquity;
        private DateTime _lastDayReset = DateTime.MinValue;

        // Circuit breaker thresholds
        private readonly decimal _maxDailyDrawdownPct;
        private readonly decimal _maxPeakDrawdownPct;

        // Stats
        public int TotalTrades { get; private set; }
        public int TotalWins { get; private set; }
        public int TotalLosses { get; private set; }
        public int ConsecutiveWins => _consecutiveWins;
        public int ConsecutiveLosses => _consecutiveLosses;
        public decimal CurrentEquity => _currentEquity;
        public decimal PeakEquity => _peakEquity;

        public TradePerformanceTracker(
            decimal initialEquity,
            decimal maxDailyDrawdownPct = 0.03m,
            decimal maxPeakDrawdownPct = 0.10m)
        {
            _currentEquity = initialEquity;
            _peakEquity = initialEquity;
            _dayStartEquity = initialEquity;
            _maxDailyDrawdownPct = maxDailyDrawdownPct;
            _maxPeakDrawdownPct = maxPeakDrawdownPct;
        }

        /// <summary>
        /// Anti-martingale size multiplier based on recent streak.
        /// Reduces size during losing streaks, modestly increases during winning streaks.
        /// </summary>
        public decimal GetSizeMultiplier()
        {
            if (_consecutiveLosses >= 3) return 0.25m;   // 75% reduction after 3+ losses
            if (_consecutiveLosses == 2) return 0.50m;    // 50% reduction after 2 losses
            if (_consecutiveLosses == 1) return 0.75m;    // 25% reduction after 1 loss
            if (_consecutiveWins >= 3) return 1.25m;      // 25% increase after 3+ wins (capped)
            if (_consecutiveWins >= 2) return 1.15m;      // 15% increase after 2 wins
            return 1.0m;                                   // baseline
        }

        /// <summary>
        /// Check if the circuit breaker is active (should stop trading).
        /// </summary>
        public bool IsCircuitBreakerActive()
        {
            if (_dayStartEquity <= 0 || _peakEquity <= 0) return false;

            // Check daily drawdown
            decimal dailyDrawdown = (_dayStartEquity - _currentEquity) / _dayStartEquity;
            if (dailyDrawdown >= _maxDailyDrawdownPct) return true;

            // Check peak drawdown
            decimal peakDrawdown = (_peakEquity - _currentEquity) / _peakEquity;
            if (peakDrawdown >= _maxPeakDrawdownPct) return true;

            return false;
        }

        /// <summary>
        /// Record a completed trade and update equity.
        /// </summary>
        public void RecordTrade(decimal pnl)
        {
            TotalTrades++;
            _currentEquity += pnl;

            if (_currentEquity > _peakEquity)
                _peakEquity = _currentEquity;

            if (pnl > 0)
            {
                TotalWins++;
                _consecutiveWins++;
                _consecutiveLosses = 0;
            }
            else if (pnl < 0)
            {
                TotalLosses++;
                _consecutiveLosses++;
                _consecutiveWins = 0;
            }
            // pnl == 0 (breakeven) doesn't break streaks
        }

        /// <summary>
        /// Reset daily equity tracking at the start of a new day.
        /// </summary>
        public void ResetDailyIfNeeded(DateTime currentTime)
        {
            if (currentTime.Date > _lastDayReset.Date)
            {
                _dayStartEquity = _currentEquity;
                _lastDayReset = currentTime;
            }
        }

        public string GetStatus()
        {
            return $"Equity:{_currentEquity:F2} Peak:{_peakEquity:F2} " +
                   $"W:{TotalWins} L:{TotalLosses} " +
                   $"Streak:W{_consecutiveWins}/L{_consecutiveLosses} " +
                   $"SizeMult:{GetSizeMultiplier():F2} CB:{IsCircuitBreakerActive()}";
        }
    }
}
