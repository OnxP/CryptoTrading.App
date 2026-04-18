using Binance;
using CryptoTrading.App.Algorithm.HtfRsiVolExpansion;
using CryptoTrading.App.Algorithm.RegimeBased;
using System;
using System.Collections.Generic;

namespace CryptoTrading.App.BackTesting
{
    internal enum TradeState { Pending, Open, Closed }

    internal class SimulatedTrade
    {
        // Setup (from the strategy signal)
        public DateTime SignalTime;          // 15M candle close that fired the signal
        public TradeDirection Direction;
        public decimal SignalPrice;          // 15M close at signal
        public decimal InitialRisk;          // 1.5 × ATR captured at signal
        public decimal AtrAtSignal;
        public double HtfRsi;
        public double VolExpansion;
        public int ProbabilityScore;

        // Sizing
        public decimal Quantity;             // base qty (5x leveraged notional / entry)
        public int Leverage;

        // Execution
        public TradeState State = TradeState.Pending;
        public DateTime EntryTime;
        public decimal EntryPrice;
        public decimal StopLoss;
        public decimal TakeProfit;
        public DateTime ExitTime;
        public decimal ExitPrice;
        public string ExitReason;
        public decimal PnlUsdt;

        // Exit state machine
        public int BarsHeld;
        public decimal HighestSinceEntry;
        public decimal LowestSinceEntry;
        public bool TrailingActive;
        public decimal TrailingStop;
    }

    internal class TradeSimulator
    {
        // Entry window: 15 1M candles after signal = one 15M bar worth
        private const int EntryWindowMinutes = 15;

        // Best-entry limit: enter if price pulls back 10% of the 1.5×ATR risk toward SL
        private const decimal EntryPullbackFraction = 0.10m;

        // Exit rules (match HtfRsiVolExpansion/SimpleExitStrategy)
        // The 15M strategy is authoritative for BOTH entries and exits.
        // Between 15M boundaries, the 1M layer only accumulates high/low for
        // trailing-stop tracking and resolves entry fills — it does NOT
        // evaluate SL/TP/trailing/time stops. Exit decisions fire only on
        // the 15M bar close, which eliminates 1M intra-bar wick whipsaws.
        private const int MaxHoldBars15M = 16;             // 4 hours on 15M
        private const decimal TrailingActivationR = 1.5m;  // activate at 1.5R profit
        private const decimal TrailingAtrMult = 1.0m;      // trail distance = 1.0 × 15M ATR

        private readonly int _leverage;
        private SimulatedTrade _active;

        public List<SimulatedTrade> Completed { get; } = new List<SimulatedTrade>();
        public bool HasActive => _active != null;

        public TradeSimulator(int leverage)
        {
            _leverage = leverage;
        }

        public void OpenFromSignal(
            DateTime signalTime,
            TradeDirection direction,
            decimal signalPrice,
            decimal stopLoss,
            decimal takeProfit,
            decimal atrAtSignal,
            decimal initialRisk,
            double htfRsi,
            double volExpansion,
            int probabilityScore,
            decimal equityUsdt)
        {
            var notional = equityUsdt * _leverage;
            var quantity = notional / signalPrice;

            _active = new SimulatedTrade
            {
                SignalTime = signalTime,
                Direction = direction,
                SignalPrice = signalPrice,
                StopLoss = stopLoss,
                TakeProfit = takeProfit,
                AtrAtSignal = atrAtSignal,
                InitialRisk = initialRisk,
                HtfRsi = htfRsi,
                VolExpansion = volExpansion,
                ProbabilityScore = probabilityScore,
                Quantity = quantity,
                Leverage = _leverage,
                State = TradeState.Pending
            };
        }

        /// <summary>
        /// Process one 1M candle with close time strictly after the signal time.
        /// Returns the completed trade when this candle closes the position,
        /// otherwise null. Active side-effects: may fill a pending trade or
        /// close an open trade.
        /// </summary>
        public SimulatedTrade Step(Candlestick m1)
        {
            if (_active == null) return null;

            if (_active.State == TradeState.Pending)
            {
                var elapsed = (m1.CloseTime - _active.SignalTime).TotalMinutes;
                if (elapsed <= 0) return null;

                // Best-entry limit price: signal price pulled back toward SL by 10% of risk.
                decimal limitPrice = _active.Direction == TradeDirection.Long
                    ? _active.SignalPrice - _active.InitialRisk * EntryPullbackFraction
                    : _active.SignalPrice + _active.InitialRisk * EntryPullbackFraction;

                // Abort if SL breached before we fill.
                bool slBreached = _active.Direction == TradeDirection.Long
                    ? m1.Low <= _active.StopLoss
                    : m1.High >= _active.StopLoss;
                if (slBreached)
                {
                    _active.EntryTime = m1.CloseTime;
                    _active.EntryPrice = limitPrice; // wouldn't have filled; cancel
                    _active.ExitTime = m1.CloseTime;
                    _active.ExitPrice = limitPrice;
                    _active.ExitReason = "EntryCancelled_SLHit";
                    _active.PnlUsdt = 0m;
                    _active.State = TradeState.Closed;
                    return Finish();
                }

                // Limit-fill attempt: if 1M candle range touches the limit.
                bool limitHit = _active.Direction == TradeDirection.Long
                    ? m1.Low <= limitPrice
                    : m1.High >= limitPrice;

                if (limitHit)
                {
                    FillEntry(limitPrice, m1.CloseTime);
                }
                else if (elapsed >= EntryWindowMinutes)
                {
                    // Window expired: market-fill at this 1M close.
                    FillEntry(m1.Close, m1.CloseTime);
                }
                else
                {
                    return null; // still waiting
                }

                // Check same-bar exit after fill (low/high can trigger SL/TP).
                return CheckExit(m1) ? Finish() : null;
            }

            if (_active.State == TradeState.Open)
            {
                return CheckExit(m1) ? Finish() : null;
            }

            return null;
        }

        private void FillEntry(decimal price, DateTime time)
        {
            // Rebase SL/TP to actual fill price, keeping 1.5× ATR distance (1:1 R:R).
            _active.EntryPrice = price;
            _active.EntryTime = time;
            var risk = _active.InitialRisk;
            if (_active.Direction == TradeDirection.Long)
            {
                _active.StopLoss = price - risk;
                _active.TakeProfit = price + risk;
                _active.HighestSinceEntry = price;
                _active.LowestSinceEntry = price;
                _active.TrailingStop = decimal.MinValue;
            }
            else
            {
                _active.StopLoss = price + risk;
                _active.TakeProfit = price - risk;
                _active.HighestSinceEntry = price;
                _active.LowestSinceEntry = price;
                _active.TrailingStop = decimal.MaxValue;
            }
            _active.State = TradeState.Open;
            _active.BarsHeld = 0;
        }

        /// <summary>
        /// Process one 1M candle while the trade is open.
        ///
        /// Between 15M boundaries: ONLY update rolling high/low. No exit check.
        /// On a 15M boundary (m1.CloseTime minute ∈ {0,15,30,45}): evaluate the
        /// full exit ladder using this 1M bar's close as the 15M close proxy.
        ///
        /// Order at 15M close: SL → trailing activation → TP (if not trailing)
        /// → trailing stop → time stop.
        ///
        /// Returns true if the trade closed on this candle.
        /// </summary>
        private bool CheckExit(Candlestick m1)
        {
            var t = _active;
            var high = m1.High;
            var low = m1.Low;
            var close = m1.Close;

            // Always accumulate path-dependent extremes so the trailing stop
            // that activates at the next 15M close sees the full interim range.
            if (high > t.HighestSinceEntry) t.HighestSinceEntry = high;
            if (low < t.LowestSinceEntry) t.LowestSinceEntry = low;

            // Exit decisions are gated to 15M bar closes.
            bool is15MBoundary = m1.CloseTime.Minute % 15 == 0;
            if (!is15MBoundary)
                return false;

            t.BarsHeld++;

            // (a) Hard stop loss — evaluated on the 15M close only. If the 15M
            // close has crossed the stop level we exit at the stop price.
            if (t.Direction == TradeDirection.Long && close <= t.StopLoss)
            {
                CloseAt(t.StopLoss, m1.CloseTime, "StopLoss");
                return true;
            }
            if (t.Direction == TradeDirection.Short && close >= t.StopLoss)
            {
                CloseAt(t.StopLoss, m1.CloseTime, "StopLoss");
                return true;
            }

            // (b) Trailing activation (at 1.5R profit, measured on 15M close)
            if (t.AtrAtSignal > 0)
            {
                var unrealized = t.Direction == TradeDirection.Long
                    ? close - t.EntryPrice
                    : t.EntryPrice - close;

                if (unrealized >= t.InitialRisk * TrailingActivationR)
                    t.TrailingActive = true;

                // (c) Take profit (only while trailing is NOT active) — on 15M close.
                if (!t.TrailingActive)
                {
                    if (t.Direction == TradeDirection.Long && close >= t.TakeProfit)
                    {
                        CloseAt(t.TakeProfit, m1.CloseTime, "TakeProfit");
                        return true;
                    }
                    if (t.Direction == TradeDirection.Short && close <= t.TakeProfit)
                    {
                        CloseAt(t.TakeProfit, m1.CloseTime, "TakeProfit");
                        return true;
                    }
                }

                // (d) Trailing stop — trail level updates from accumulated extremes,
                //     exit triggered on the 15M close breaching the trail.
                if (t.TrailingActive)
                {
                    var trailDist = t.AtrAtSignal * TrailingAtrMult;
                    if (t.Direction == TradeDirection.Long)
                    {
                        var newTrail = t.HighestSinceEntry - trailDist;
                        if (newTrail > t.TrailingStop) t.TrailingStop = newTrail;
                        if (close <= t.TrailingStop)
                        {
                            CloseAt(t.TrailingStop, m1.CloseTime, "TrailingStop");
                            return true;
                        }
                    }
                    else
                    {
                        var newTrail = t.LowestSinceEntry + trailDist;
                        if (newTrail < t.TrailingStop) t.TrailingStop = newTrail;
                        if (close >= t.TrailingStop)
                        {
                            CloseAt(t.TrailingStop, m1.CloseTime, "TrailingStop");
                            return true;
                        }
                    }
                }
            }

            // (e) Time stop — 4 hours = 16 × 15M bars.
            if (t.BarsHeld >= MaxHoldBars15M)
            {
                CloseAt(close, m1.CloseTime, "TimeStop");
                return true;
            }

            return false;
        }

        private void CloseAt(decimal price, DateTime time, string reason)
        {
            var t = _active;
            t.ExitPrice = price;
            t.ExitTime = time;
            t.ExitReason = reason;

            var priceMove = t.Direction == TradeDirection.Long
                ? price - t.EntryPrice
                : t.EntryPrice - price;

            t.PnlUsdt = priceMove * t.Quantity * t.Leverage;
            t.State = TradeState.Closed;
        }

        /// <summary>Force-close any still-open trade at the given price/time.</summary>
        public SimulatedTrade ForceCloseAtEnd(decimal price, DateTime time)
        {
            if (_active == null) return null;
            if (_active.State == TradeState.Pending)
            {
                _active.EntryTime = time;
                _active.EntryPrice = price;
                _active.ExitTime = time;
                _active.ExitPrice = price;
                _active.ExitReason = "EntryCancelled_Eof";
                _active.PnlUsdt = 0m;
                _active.State = TradeState.Closed;
            }
            else if (_active.State == TradeState.Open)
            {
                CloseAt(price, time, "EndOfData");
            }
            return Finish();
        }

        private SimulatedTrade Finish()
        {
            var done = _active;
            _active = null;
            Completed.Add(done);
            return done;
        }
    }
}
