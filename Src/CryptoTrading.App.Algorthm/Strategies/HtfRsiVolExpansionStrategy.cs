using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Strategies
{
    /// <summary>
    /// HTF RSI Direction + LTF Volatility Expansion Strategy
    ///
    /// Multi-timeframe strategy:
    ///   - 4H SMA-RSI(14) determines trade direction (>65 long, <35 short)
    ///   - 15M ATR(14) expansion (>1.2x vs 20 candles ago) triggers entry
    ///   - Symmetrical 1.5x ATR TP/SL (1:1 R:R)
    ///   - Trailing stop at 1.5R, trails 1.0x ATR
    ///   - 3-loss cooldown pauses 16 candles
    ///   - All trades at 5x leverage
    ///
    /// CRITICAL: The 4H RSI uses SMA-based calculation, NOT Wilder smoothing.
    ///   SMA RSI recalculates avg gain/loss from scratch using the last 14 bars each time.
    ///   Wilder RSI uses exponential smoothing: avgGain = (prev * 13 + current) / 14.
    ///   These produce DIFFERENT values. Using Wilder will not reproduce the backtest.
    ///   425 out of 1,630 4H bars give different trade signals between the two methods.
    ///
    /// Backtest (BTCUSDT 15M, Jul 2025 - Mar 2026):
    ///   436 trades, 63.3% WR, PF 1.90, +2,675% at 5x, 20.9% max DD, 9/9 months green
    ///
    /// Verification checkpoint:
    ///   4H bar index 14 (15M rows 224-239, 2025-07-03 08:00) → SMA RSI = 67.07
    ///   Trade 1: Long entry at 110,260.86 on 2025-07-03 09:45 (row 231)
    /// </summary>
    public class HtfRsiVolExpansionStrategy
    {
        // ============================================================
        // PARAMETERS
        // ============================================================

        /// <summary>Number of 15M candles per HTF bar. 16 = 4 hours.</summary>
        public int HtfMultiplier { get; set; } = 16;

        /// <summary>RSI period on the 4H timeframe</summary>
        public int HtfRsiPeriod { get; set; } = 14;

        /// <summary>4H RSI above this → long bias</summary>
        public double RsiLongThreshold { get; set; } = 65;

        /// <summary>4H RSI below this → short bias</summary>
        public double RsiShortThreshold { get; set; } = 35;

        /// <summary>ATR period on the 15M timeframe</summary>
        public int LtfAtrPeriod { get; set; } = 14;

        /// <summary>Current ATR must exceed ATR from N candles ago by this ratio</summary>
        public double VolExpansionRatio { get; set; } = 1.2;

        /// <summary>How many 15M candles back to compare ATR against</summary>
        public int VolExpansionLookback { get; set; } = 20;

        /// <summary>Take profit distance as multiple of current ATR</summary>
        public double TpAtrMult { get; set; } = 1.5;

        /// <summary>Stop loss distance as multiple of current ATR</summary>
        public double SlAtrMult { get; set; } = 1.5;

        /// <summary>Trailing stop activates when profit reaches this many R-multiples</summary>
        public double TrailActivationR { get; set; } = 1.5;

        /// <summary>Trailing stop distance as multiple of current ATR</summary>
        public double TrailDistAtrMult { get; set; } = 1.0;

        /// <summary>Close position after this many 15M candles</summary>
        public int MaxHoldCandles { get; set; } = 16;

        /// <summary>Minimum 15M candles between trade exit and next entry</summary>
        public int MinGapCandles { get; set; } = 8;

        /// <summary>Enter cooldown after this many consecutive losses</summary>
        public int CooldownAfterLosses { get; set; } = 3;

        /// <summary>Cooldown duration in 15M candles</summary>
        public int CooldownDurationCandles { get; set; } = 16;

        /// <summary>Leverage for all trades</summary>
        public double Leverage { get; set; } = 5.0;

        // ============================================================
        // INTERNAL STATE
        // ============================================================

        // 4H bar construction
        private readonly List<HtfBar> _htfBars = new();
        private readonly List<LtfCandle> _ltfBuffer = new();

        // 15M indicator history
        private readonly List<double> _ltfAtrHistory = new();
        private readonly List<double> _ltfTrueRanges = new();
        private readonly List<double> _ltfCloses = new();

        // Trade management
        private OpenTrade _currentTrade;
        private int _lastExitIndex = -100;
        private readonly List<bool> _tradeResults = new(); // true=win, false=loss

        // ============================================================
        // MAIN ENTRY POINT
        // ============================================================

        /// <summary>
        /// Process a new 15M candle. Returns a signal if action is needed.
        /// Call this on every 15M candle in sequence.
        /// </summary>
        public TradeSignal OnCandle(double open, double high, double low, double close,
                                     double volume, DateTime timestamp, int candleIndex)
        {
            // 1. Update 15M indicators
            UpdateLtfIndicators(open, high, low, close);

            // 2. Aggregate into 4H bar
            _ltfBuffer.Add(new LtfCandle { O = open, H = high, L = low, C = close, V = volume, Dt = timestamp });

            if (_ltfBuffer.Count >= HtfMultiplier)
            {
                BuildHtfBar();
                _ltfBuffer.Clear();
            }

            // 3. Not enough data yet
            if (_htfBars.Count < HtfRsiPeriod + 1)
                return TradeSignal.NoAction();

            if (_ltfAtrHistory.Count <= VolExpansionLookback)
                return TradeSignal.NoAction();

            // 4. Manage existing position
            if (_currentTrade != null)
                return ManagePosition(high, low, close, candleIndex);

            // 5. Check for new entry
            return CheckEntry(open, high, low, close, volume, timestamp, candleIndex);
        }

        // ============================================================
        // 4H BAR CONSTRUCTION
        // ============================================================

        private void BuildHtfBar()
        {
            var bar = new HtfBar
            {
                Index = _htfBars.Count,
                Dt = _ltfBuffer[0].Dt,
                Open = _ltfBuffer[0].O,
                High = _ltfBuffer.Max(c => c.H),
                Low = _ltfBuffer.Min(c => c.L),
                Close = _ltfBuffer[^1].C,
                Volume = _ltfBuffer.Sum(c => c.V),
            };

            _htfBars.Add(bar);

            // Compute SMA-based RSI AFTER adding to list
            bar.Rsi = ComputeHtfSmaRsi();
        }

        // ============================================================
        // 4H SMA-BASED RSI
        // ============================================================
        //
        // CRITICAL: This is NOT Wilder-smoothed RSI.
        //
        // For each bar, we look at the last 14 close-to-close changes
        // and compute avg gain and avg loss FROM SCRATCH.
        // There is no carry-forward from the previous bar.
        //
        // SMA RSI:    avg_gain = sum(gains over last 14 bars) / 14
        // Wilder RSI: avg_gain = (prev_avg_gain * 13 + current_gain) / 14
        //
        // The SMA method is more reactive — it swings further from 50,
        // producing more extreme readings that trigger more trades.
        //
        // On the backtest data, 425 out of 1,630 bars produce a
        // DIFFERENT trade signal between SMA and Wilder.

        private double? ComputeHtfSmaRsi()
        {
            int count = _htfBars.Count;
            if (count < HtfRsiPeriod + 1)
                return null;

            // Look at the last 14 close-to-close changes
            double sumGain = 0;
            double sumLoss = 0;

            for (int j = 0; j < HtfRsiPeriod; j++)
            {
                int idx = count - 1 - j;       // current bar in this pair
                int prevIdx = idx - 1;          // previous bar

                double change = _htfBars[idx].Close - _htfBars[prevIdx].Close;

                if (change > 0)
                    sumGain += change;
                else
                    sumLoss += Math.Abs(change);
            }

            double avgGain = sumGain / HtfRsiPeriod;
            double avgLoss = sumLoss / HtfRsiPeriod;

            if (avgLoss <= 0)
                return 100.0;

            double rs = avgGain / avgLoss;
            return 100.0 - (100.0 / (1.0 + rs));
        }

        // ============================================================
        // 15M ATR CALCULATION
        // ============================================================

        private void UpdateLtfIndicators(double open, double high, double low, double close)
        {
            double prevClose = _ltfCloses.Count > 0 ? _ltfCloses[^1] : close;
            _ltfCloses.Add(close);

            // True Range
            double tr = Math.Max(
                high - low,
                Math.Max(
                    Math.Abs(high - prevClose),
                    Math.Abs(low - prevClose)
                )
            );

            _ltfTrueRanges.Add(tr);

            // ATR = simple average of last N true ranges
            if (_ltfTrueRanges.Count >= LtfAtrPeriod)
            {
                double atr = 0;
                for (int i = _ltfTrueRanges.Count - LtfAtrPeriod; i < _ltfTrueRanges.Count; i++)
                    atr += _ltfTrueRanges[i];
                atr /= LtfAtrPeriod;
                _ltfAtrHistory.Add(atr);
            }
            else
            {
                _ltfAtrHistory.Add(tr); // not enough data, use raw TR
            }
        }

        // ============================================================
        // ENTRY LOGIC
        // ============================================================

        private TradeSignal CheckEntry(double open, double high, double low, double close,
                                        double volume, DateTime timestamp, int candleIndex)
        {
            // Gap check
            if (candleIndex - _lastExitIndex < MinGapCandles)
                return TradeSignal.NoAction();

            // Get current 4H RSI
            // The active 4H bar is the LAST completed bar
            var currentHtfBar = _htfBars[^1];
            double? htfRsi = currentHtfBar.Rsi;
            if (htfRsi == null)
                return TradeSignal.NoAction();

            // Determine direction bias
            string bias;
            if (htfRsi.Value > RsiLongThreshold)
                bias = "long";
            else if (htfRsi.Value < RsiShortThreshold)
                bias = "short";
            else
                return TradeSignal.NoAction(); // RSI in neutral zone (35-65)

            // Cooldown check
            if (IsInCooldown(candleIndex))
                return TradeSignal.NoAction();

            // Vol expansion check
            double atrNow = _ltfAtrHistory[^1];
            int prevIdx = _ltfAtrHistory.Count - 1 - VolExpansionLookback;
            if (prevIdx < 0)
                return TradeSignal.NoAction();

            double atrPrev = _ltfAtrHistory[prevIdx];
            if (atrPrev <= 0)
                return TradeSignal.NoAction();

            double volExpansion = atrNow / atrPrev;
            if (volExpansion < VolExpansionRatio)
                return TradeSignal.NoAction();

            // Calculate SL/TP
            double entryPrice = close;
            double slDist = SlAtrMult * atrNow;
            double tpDist = TpAtrMult * atrNow;

            double stopLoss, takeProfit;
            if (bias == "long")
            {
                stopLoss = entryPrice - slDist;
                takeProfit = entryPrice + tpDist;
            }
            else
            {
                stopLoss = entryPrice + slDist;
                takeProfit = entryPrice - tpDist;
            }

            // Open the trade
            _currentTrade = new OpenTrade
            {
                Direction = bias,
                EntryPrice = entryPrice,
                StopLoss = stopLoss,
                TakeProfit = takeProfit,
                Risk = slDist,
                TrailingStop = null,
                EntryIndex = candleIndex,
                EntryTime = timestamp,
                HtfRsi = htfRsi.Value,
                AtrAtEntry = atrNow,
                AtrPrev = atrPrev,
                VolExpansion = volExpansion,
            };

            return new TradeSignal
            {
                Action = bias == "long" ? "BUY" : "SELL",
                Price = entryPrice,
                StopLoss = stopLoss,
                TakeProfit = takeProfit,
                Leverage = Leverage,
                Direction = bias,
                Reason = $"4H_RSI={htfRsi.Value:F1} ATR_Exp={volExpansion:F2}x " +
                         $"ATR={atrNow:F2} SL={stopLoss:F2} TP={takeProfit:F2}",
            };
        }

        // ============================================================
        // POSITION MANAGEMENT
        // ============================================================

        private TradeSignal ManagePosition(double high, double low, double close, int candleIndex)
        {
            var trade = _currentTrade;
            int held = candleIndex - trade.EntryIndex;
            bool isShort = trade.Direction == "short";

            double unrealisedPnl = isShort
                ? trade.EntryPrice - close
                : close - trade.EntryPrice;

            // --- STOP LOSS ---
            // Check against candle high (shorts) or low (longs), not close
            bool slHit = isShort
                ? high >= trade.StopLoss
                : low <= trade.StopLoss;

            if (slHit)
            {
                double exitPrice = trade.StopLoss;
                double pnl = isShort ? trade.EntryPrice - exitPrice : exitPrice - trade.EntryPrice;
                return CloseTrade(exitPrice, "sl", pnl, candleIndex, isWin: false);
            }

            // --- TAKE PROFIT ---
            // Check against candle low (shorts) or high (longs)
            bool tpHit = isShort
                ? low <= trade.TakeProfit
                : high >= trade.TakeProfit;

            if (tpHit)
            {
                double exitPrice = trade.TakeProfit;
                double pnl = isShort ? trade.EntryPrice - exitPrice : exitPrice - trade.EntryPrice;
                return CloseTrade(exitPrice, "tp", pnl, candleIndex, isWin: true);
            }

            // --- TRAILING STOP ---
            // Activate when unrealised profit reaches TrailActivationR × risk
            if (unrealisedPnl >= trade.Risk * TrailActivationR)
            {
                double currentAtr = _ltfAtrHistory[^1];
                double trailDist = TrailDistAtrMult * currentAtr;

                if (isShort)
                {
                    double newTrail = low + trailDist;
                    // Only move trailing stop DOWN for shorts (tighter)
                    if (trade.TrailingStop == null || newTrail < trade.TrailingStop.Value)
                        trade.TrailingStop = newTrail;
                }
                else
                {
                    double newTrail = high - trailDist;
                    // Only move trailing stop UP for longs (tighter)
                    if (trade.TrailingStop == null || newTrail > trade.TrailingStop.Value)
                        trade.TrailingStop = newTrail;
                }
            }

            // Check if trailing stop is hit
            if (trade.TrailingStop.HasValue && unrealisedPnl > 0)
            {
                bool trailHit = isShort
                    ? high >= trade.TrailingStop.Value
                    : low <= trade.TrailingStop.Value;

                if (trailHit)
                {
                    double exitPrice = trade.TrailingStop.Value;
                    double pnl = isShort ? trade.EntryPrice - exitPrice : exitPrice - trade.EntryPrice;
                    return CloseTrade(exitPrice, "trail", pnl, candleIndex, isWin: true);
                }
            }

            // --- TIME STOP ---
            // Close at market after MaxHoldCandles
            if (held >= MaxHoldCandles)
            {
                double exitPrice = close;
                double pnl = isShort ? trade.EntryPrice - exitPrice : exitPrice - trade.EntryPrice;
                return CloseTrade(exitPrice, "time", pnl, candleIndex, isWin: pnl > 0);
            }

            return TradeSignal.NoAction();
        }

        private TradeSignal CloseTrade(double exitPrice, string reason, double pnl,
                                        int candleIndex, bool isWin)
        {
            var trade = _currentTrade;
            string closeAction = trade.Direction == "long" ? "SELL" : "BUY";

            _tradeResults.Add(isWin);
            _lastExitIndex = candleIndex;
            _currentTrade = null;

            return new TradeSignal
            {
                Action = closeAction,
                Price = exitPrice,
                IsClose = true,
                Pnl = pnl,
                Direction = trade.Direction,
                Reason = $"EXIT_{reason} PnL={pnl:+0;-0}pts " +
                         $"Entry={trade.EntryPrice:F2} 4HRSI={trade.HtfRsi:F1} " +
                         $"ATR_Exp={trade.VolExpansion:F2}x",
            };
        }

        // ============================================================
        // COOLDOWN LOGIC
        // ============================================================

        private bool IsInCooldown(int currentIndex)
        {
            if (_tradeResults.Count < CooldownAfterLosses)
                return false;

            // Check if last N results are all losses
            for (int i = 0; i < CooldownAfterLosses; i++)
            {
                if (_tradeResults[_tradeResults.Count - 1 - i])
                    return false; // found a win, no cooldown needed
            }

            // All recent trades were losses — check if cooldown period has elapsed
            return currentIndex - _lastExitIndex < CooldownDurationCandles;
        }

        // ============================================================
        // RESET
        // ============================================================

        /// <summary>Clear all state. Call before starting a new backtest or session.</summary>
        public void Reset()
        {
            _htfBars.Clear();
            _ltfBuffer.Clear();
            _ltfAtrHistory.Clear();
            _ltfTrueRanges.Clear();
            _ltfCloses.Clear();
            _tradeResults.Clear();
            _currentTrade = null;
            _lastExitIndex = -100;
        }

        // ============================================================
        // PRIVATE DATA TYPES
        // ============================================================

        private class HtfBar
        {
            public int Index;
            public DateTime Dt;
            public double Open, High, Low, Close, Volume;
            public double? Rsi;
        }

        private class LtfCandle
        {
            public double O, H, L, C, V;
            public DateTime Dt;
        }

        private class OpenTrade
        {
            public string Direction;     // "long" or "short"
            public double EntryPrice;
            public double StopLoss;
            public double TakeProfit;
            public double Risk;          // SL distance in points
            public double? TrailingStop;
            public int EntryIndex;       // 15M candle index at entry
            public DateTime EntryTime;
            public double HtfRsi;
            public double AtrAtEntry;
            public double AtrPrev;
            public double VolExpansion;
        }
    }

    // ============================================================
    // TRADE SIGNAL
    // Replace with your existing signal/order type if you have one
    // ============================================================

    public class TradeSignal
    {
        /// <summary>"BUY", "SELL", or "NONE"</summary>
        public string Action { get; set; } = "NONE";

        /// <summary>Entry or exit price</summary>
        public double Price { get; set; }

        /// <summary>Stop loss level (entry signals only)</summary>
        public double StopLoss { get; set; }

        /// <summary>Take profit level (entry signals only)</summary>
        public double TakeProfit { get; set; }

        /// <summary>Leverage to use (entry signals only)</summary>
        public double Leverage { get; set; }

        /// <summary>"long" or "short"</summary>
        public string Direction { get; set; }

        /// <summary>True if this signal closes a position</summary>
        public bool IsClose { get; set; }

        /// <summary>Realised P&L in points (close signals only)</summary>
        public double Pnl { get; set; }

        /// <summary>Human-readable reason</summary>
        public string Reason { get; set; }

        public static TradeSignal NoAction() => new() { Action = "NONE" };
        public bool HasAction => Action != "NONE";
    }
}
