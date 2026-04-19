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

        // 30M structure-break exit (Phase 1 postmortem feature).
        // Low/High of the last completed 30M bar at/before signal time.
        // A 30M close beyond this level (below for long, above for short)
        // triggers an early exit — the single strongest leading indicator
        // of "pure losses" found in the Phase 1 postmortem (catch rate
        // 59.7% Pure vs 40.5% false-positive on Recoverable).
        public decimal Entry30mBarLow;
        public decimal Entry30mBarHigh;

        // Fakeout-exit state (Phase 2 postmortem feature).
        // Tracks whether the first 15M close after entry was adverse, and
        // the peak favorable excursion (in R) seen through each 15M close.
        // Used by the "Bar1 adverse + MFE<0.25R by bar 2" early-exit rule.
        public bool Bar1ClosedAdverse;
        public decimal MfePeakR;

        // Adaptive-entry state. Number of 15M boundaries crossed while the
        // trade was Pending. Used to step through the deepening pullback-
        // limit schedule when the user wants to wait 1-2 bars for a better
        // fill instead of market-filling at the end of bar 1.
        public int PendingBars;

        // Classifier-routed entry (Phase 3). Extension = how far SignalPrice
        // is from the 15M EMA20, expressed in 15M-ATR units. Positive means
        // stretched in the trade direction. EntryMode picks Early / Neutral /
        // Late based on extension and drives which fill path runs in Step().
        public decimal Ema20_15M_AtSignal;
        public decimal Extension_AtSignal;
        public string EntryMode;            // "Early" | "Neutral" | "Late" | "Aligned" | "Range" | "Opposed"

        // 15M regime classifier (MACD + Bollinger Bands) state captured at
        // signal time. EffectiveLeverage may be less than Leverage when the
        // regime dictates reduced sizing (Opposed). Quantity is sized off
        // EffectiveLeverage, not the sim's base leverage.
        public decimal MacdHist_AtSignal;
        public decimal BBUpper_AtSignal;
        public decimal BBMiddle_AtSignal;
        public decimal BBLower_AtSignal;
        public decimal BBW_AtSignal;        // (upper - lower) / middle
        public decimal EffectiveLeverage;
        public string Regime;               // "Aligned" | "Range" | "Opposed"
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

        // 30M structure-break early exit. DISABLED after regression test —
        // the Phase 1 SL postmortem showed 60% of pure losses break 30M
        // structure vs 40% of recoverables (promising Pure-vs-Rec signal),
        // but a cross-check on WINNERS (not in the original postmortem)
        // showed the rule also fires on 21.6% of winning trades. On
        // Phase 3 full-year backtest enabling it crashed net P&L from
        // +$43.77M to +$755K — the winner kill outweighs the loss save.
        // Plumbing retained so tighter variants (e.g. requiring RSI
        // confirmation, N consecutive breaks, or score-gating) can be
        // tested without re-wiring.
        private const bool EnableStructure30mExit = false;

        // Fakeout early exit. At the close of bar 2 (second 15M boundary after
        // entry), exit if:
        //   - bar 1 closed adverse (first 15M close was on the wrong side of
        //     our entry), AND
        //   - peak favorable excursion through bar 2's close is < 0.25 R.
        //
        // Phase 2 postmortem on 271 filled trades showed this combination
        // fires on 27.3% of losing trades (20 Pure + 10 Recoverable) and 0%
        // of winners by construction — winners by definition reach at least
        // 1R favor, so they cannot have final MFE < 0.25R. The LIVE rule
        // checks MFE-so-far at bar 2, which is a slightly tighter bound,
        // so some winners whose bar 1 was adverse AND which hadn't yet
        // recovered to 0.25R by bar 2 will be killed. Flat-return expected
        // value was +$11M on that sample; compounding may alter that, which
        // is the whole point of running the full backtest now.
        private const bool EnableFakeoutExit = false;
        private const decimal FakeoutMfeThresholdR = 0.25m;

        // Adaptive entry (Phase 2 – the "wait for best buy if price is falling"
        // question). Default behavior market-fills at bar 1 close if the 0.10R
        // pullback limit didn't hit. Adaptive behavior instead:
        //   - Bar 1: limit at 0.10R pullback (same as before).
        //   - Bar 2: if bar 1 closed adverse AND no fill, deepen limit to 0.25R.
        //   - Bar 3: deepen to 0.50R.
        //   - End of bar 3: cancel the signal.
        // Escape hatch: if at any 15M close price has rallied past signalPrice
        // + 0.10R in our direction, the signal has already worked without us —
        // market-fill at that close ("chase") rather than miss the move.
        private const bool EnableAdaptiveEntry = true;
        private const int AdaptiveMaxBars = 3;
        private static readonly decimal[] AdaptiveLimitFrac =
            new[] { 0.10m, 0.25m, 0.50m };
        private const decimal AdaptiveChaseFrac = 0.10m;

        // Classifier-routed entry (Phase 3). Routes each signal to one of
        // three entry paths based on how extended price is from its 15M EMA20,
        // measured in 15M-ATR units:
        //   Early  (Ext < 0.5):   market-fill at first 1M bar after signal.
        //   Neutral(0.5 <= <1.5): 0.10R pullback limit, 1×15M budget,
        //                         market fallback on bar expiry.
        //   Late   (Ext >= 1.5):  deepening 0.10 -> 0.25R limit, GATED on
        //                         1M RSI exhaustion trigger, 2×15M budget,
        //                         cancel if trigger never fires.
        // Chase escape (signal moved AdaptiveChaseFrac × R our way without us)
        // fires in Neutral and Late alike.
        // v1 extension-based classifier. Disabled in favor of the regime
        // classifier below, which uses 15M MACD + BB instead of a single
        // EMA20-extension measure.
        private const bool EnableClassifier = false;
        private const decimal ClassifierEarlyMin = 0.0m;
        private const decimal ClassifierLateMin  = 1.5m;

        // 15M regime classifier (MACD + Bollinger Bands). Examines MACD
        // histogram sign against signal direction and BB width vs a threshold
        // to split signals into three buckets:
        //
        //   Aligned  – MACD histogram same sign as signal direction.
        //              Trend is with us → market-fill ASAP at full leverage.
        //   Range    – MACD opposing AND BB width < RangeBBWMax.
        //              Narrow bands with a micro-counter push → wait for
        //              price to touch the BB band on our side (long: lower,
        //              short: upper) for a discount fill. Budget 2×15M bars,
        //              then market-fill on expiry rather than skip.
        //   Opposed  – MACD opposing AND BB wide (real counter-trend).
        //              Dangerous signal — reduce leverage to 2× from 5× and
        //              market-fill ASAP.
        //
        // RangeBBWMax is BB width expressed as a fraction of BB middle. 2.5%
        // is a common threshold separating compressed from expanded BTC 15M.
        private const bool EnableRegimeClassifier = false;
        private const decimal RangeBBWMax = 0.025m;
        private const decimal OpposedLeverageMult = 0.4m;  // 5× × 0.4 = 2× effective

        // Entry-strategy A/B test. When non-empty, forces every signal to use
        // the named StepXxx path — bypasses EnableClassifier / EnableRegime
        // entirely. Lets us isolate the effect of 1M entry search vs market-
        // fill on an otherwise-identical signal stream.
        //   ""         → normal routing (Neutral when classifiers off)
        //   "Early"    → market-fill at first 1M close after signal
        //   "Neutral"  → 0.10R pullback limit, 1×15M budget, else market-fill
        //   "Late"     → 0.10→0.25R deepening + 1M RSI turn + 2×15M, else cancel
        //   "Srsi"     → 1M Stochastic RSI(14,14,3,3): fill when K&D oversold
        //                (long <30) or overbought (short >70), 2×15M budget,
        //                else cancel
        //   "Bb"       → 1M Bollinger Bands(20,2): fill when 1M low touches
        //                lower band (long) / high touches upper band (short),
        //                2×15M budget, else cancel
        //   "BbGuide"  → 1M price-direction: fill when climbing (long) /
        //                falling (short). Wait if moving against us; 2×15M
        //                budget; market-fill on expiry (no cancel).
        private const string ForcedEntryMode = "BbGuide";

        // Late-mode 1M RSI trigger. Wilder RSI(14) on 1M closes.
        private const int Rsi1mPeriod = 14;
        private const decimal LateRsiLongMax  = 40m;  // long fires when RSI[t]<40 and turning up
        private const decimal LateRsiShortMin = 60m;  // short fires when RSI[t]>60 and turning down

        // Loss-clustering cooldown. Disabled while testing the regime
        // classifier in isolation so its standalone effect is visible.
        private const bool EnableLossCooldown = false;
        private const int LossCooldownTriggerCount = 2;    // N = consecutive losses to activate
        private const int LossCooldownSkipCount    = 1;    // M = signals to skip once active

        private readonly int _leverage;
        private SimulatedTrade _active;

        // Public cooldown state. Program.cs checks InCooldown before calling
        // OpenFromSignal and, if true, decrements the remaining-skip counter
        // and does NOT open the trade. This is the cheapest place to gate
        // signals without re-plumbing the algorithm.
        private int _consecutiveLosses;
        private int _remainingSkips;
        public bool InCooldown => _remainingSkips > 0;
        public void ConsumeCooldownSkip() { if (_remainingSkips > 0) _remainingSkips--; }

        // Continuous 1M Wilder RSI(14) state — updated on every 1M bar seen,
        // regardless of whether a trade is active. Late-mode trigger reads
        // _rsi1mCurrent / _rsi1mPrev to detect a local exhaustion turn.
        private decimal _prev1mClose;
        private decimal _rsi1mAvgGain;
        private decimal _rsi1mAvgLoss;
        private int _rsi1mBarsSeen;
        private decimal _rsi1mCurrent;
        private decimal _rsi1mPrev;

        // Stochastic RSI(14, 14, 3, 3) on 1M closes — a proper 1M entry-
        // timing indicator built on top of the running Wilder RSI.
        //   stochRSI = (RSI − min14(RSI)) / (max14(RSI) − min14(RSI)) × 100
        //   %K = SMA(stochRSI, 3)
        //   %D = SMA(%K, 3)
        // Srsi-mode entry waits for BOTH lines to be in the oversold/overbought
        // zone (long: <30, short: >70) before market-filling. The buffers
        // keep the last 14 RSI values, last 3 stochRSI values (for K), and
        // last 3 K values (for D). Circular indices avoid list allocations.
        private const int SrsiStochPeriod = 14;
        private const int SrsiSmoothK = 3;
        private const int SrsiSmoothD = 3;
        private const decimal SrsiLongMax = 30m;
        private const decimal SrsiShortMin = 70m;
        private readonly decimal[] _srsiRsiBuf = new decimal[SrsiStochPeriod];
        private readonly decimal[] _srsiKBuf = new decimal[SrsiSmoothK];
        private readonly decimal[] _srsiDBuf = new decimal[SrsiSmoothD];
        private int _srsiRsiCount, _srsiKCount, _srsiDCount;
        private int _srsiRsiIdx, _srsiKIdx, _srsiDIdx;
        private decimal _srsiK;
        private decimal _srsiD;

        // 1M Bollinger Bands(20, 2) — ring buffer of the last 20 closes with
        // population-stddev bands. Used by the "Bb" entry helper to identify
        // a 1M extreme on our side of the trade: long fills on a lower-band
        // touch, short fills on an upper-band touch. Updated on every 1M bar
        // so Bb-mode has state ready the instant a trade opens.
        private const int Bb1mPeriod = 20;
        private const decimal Bb1mSigma = 2m;
        private readonly decimal[] _bb1mWindow = new decimal[Bb1mPeriod];
        private int _bb1mFill, _bb1mIndex;
        private decimal _bb1mMiddle, _bb1mUpper, _bb1mLower;
        private bool Bb1mWarm => _bb1mFill >= Bb1mPeriod;

        // Prev-close tracker for the BbGuide entry mode. Captures the 1M
        // close from the preceding bar so BbGuide can compare current close
        // against it to decide "climbing" vs "falling." Updated at the top
        // of Step() on every 1M bar the simulator sees, whether a trade is
        // active or not, so it is warm the moment a new signal fires.
        private decimal _priorClose1m;
        private bool _priorClose1mSet;

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
            decimal equityUsdt,
            decimal entry30mBarLow,
            decimal entry30mBarHigh,
            decimal ema20_15M,
            decimal macdHist,
            decimal bbMiddle,
            decimal bbUpper,
            decimal bbLower)
        {
            // Extension (legacy classifier). Kept in the CSV for continued
            // postmortem analysis even when the regime classifier drives mode.
            decimal extension = 0m;
            if (atrAtSignal > 0m && ema20_15M > 0m)
            {
                decimal rawAbove = (signalPrice - ema20_15M) / atrAtSignal;
                extension = direction == TradeDirection.Long ? rawAbove : -rawAbove;
            }

            // Regime classification from 15M MACD + Bollinger Bands.
            //   macdAligned: hist sign matches signal direction
            //   bbwNarrow:   (upper - lower) / middle < RangeBBWMax
            decimal bbw = bbMiddle > 0m ? (bbUpper - bbLower) / bbMiddle : 0m;
            bool macdAligned = direction == TradeDirection.Long ? macdHist > 0m : macdHist < 0m;

            string regime;
            decimal levMult;
            if (!EnableRegimeClassifier)
            {
                regime = "Aligned"; levMult = 1m;
            }
            else if (macdAligned)
            {
                regime = "Aligned"; levMult = 1m;
            }
            else if (bbw > 0m && bbw < RangeBBWMax)
            {
                regime = "Range"; levMult = 1m;
            }
            else
            {
                regime = "Opposed"; levMult = OpposedLeverageMult;
            }

            decimal effectiveLeverage = _leverage * levMult;
            var notional = equityUsdt * effectiveLeverage;
            var quantity = notional / signalPrice;

            // EntryMode map: regime drives which Step helper runs.
            //   Aligned  → Early  (market fill ASAP)
            //   Range    → Range  (wait for BB extreme on our side)
            //   Opposed  → Early  (market fill; reduced leverage handles risk)
            string entryMode;
            if (!string.IsNullOrEmpty(ForcedEntryMode))
                entryMode = ForcedEntryMode;
            else if (EnableRegimeClassifier)
                entryMode = regime == "Range" ? "Range" : "Early";
            else if (!EnableClassifier)
                entryMode = "Neutral";
            else if (extension >= ClassifierLateMin)
                entryMode = "Late";
            else if (extension >= ClassifierEarlyMin)
                entryMode = "Early";
            else
                entryMode = "Late";

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
                State = TradeState.Pending,
                Entry30mBarLow = entry30mBarLow,
                Entry30mBarHigh = entry30mBarHigh,
                Ema20_15M_AtSignal = ema20_15M,
                Extension_AtSignal = extension,
                EntryMode = entryMode,
                MacdHist_AtSignal = macdHist,
                BBUpper_AtSignal = bbUpper,
                BBMiddle_AtSignal = bbMiddle,
                BBLower_AtSignal = bbLower,
                BBW_AtSignal = bbw,
                EffectiveLeverage = effectiveLeverage,
                Regime = regime
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
            // Always keep the continuous 1M RSI(14) current. This runs every
            // 1M bar the simulator observes — including bars with no active
            // trade — so that Late-mode has a warm RSI series to read from
            // the instant a trade is opened.
            // Capture the prior 1M close BEFORE updating any indicator state
            // so BbGuide has bar-N-1 to compare against the current bar.
            decimal previousClose = _priorClose1mSet ? _priorClose1m : m1.Close;
            _priorClose1m = m1.Close;
            _priorClose1mSet = true;

            UpdateRsi1M(m1.Close);
            UpdateBb1M(m1.Close);

            if (_active == null) return null;

            if (_active.State == TradeState.Pending)
            {
                var elapsed = (m1.CloseTime - _active.SignalTime).TotalMinutes;
                if (elapsed <= 0) return null;

                // Abort if SL breached before we fill — common guard for all
                // entry modes.
                bool slBreached = _active.Direction == TradeDirection.Long
                    ? m1.Low <= _active.StopLoss
                    : m1.High >= _active.StopLoss;
                if (slBreached)
                {
                    _active.EntryTime = m1.CloseTime;
                    _active.EntryPrice = m1.Close;
                    _active.ExitTime = m1.CloseTime;
                    _active.ExitPrice = m1.Close;
                    _active.ExitReason = "EntryCancelled_SLHit";
                    _active.PnlUsdt = 0m;
                    _active.State = TradeState.Closed;
                    return Finish();
                }

                // Route by classifier mode.
                return _active.EntryMode switch
                {
                    "Early"   => StepEarly(m1),
                    "Late"    => StepLate(m1),
                    "Range"   => StepRange(m1),
                    "Srsi"    => StepSrsi(m1),
                    "Bb"      => StepBb(m1),
                    "BbGuide" => StepBbGuide(m1, previousClose),
                    _         => StepNeutral(m1),
                };
            }

            if (_active.State == TradeState.Open)
            {
                return CheckExit(m1) ? Finish() : null;
            }

            return null;
        }

        // ---- Classifier-routed entry paths -------------------------------

        /// <summary>
        /// Early mode: signal fired close to the 15M EMA20 — the move is just
        /// starting, no pullback is expected. Market-fill on the first 1M bar
        /// after signal.
        /// </summary>
        private SimulatedTrade StepEarly(Candlestick m1)
        {
            FillEntry(m1.Close, m1.CloseTime);
            return CheckExit(m1) ? Finish() : null;
        }

        /// <summary>
        /// Neutral mode: single 0.10R pullback limit for one 15M bar, then
        /// market-fill at window expiry. Chase escape and SL abort apply.
        /// </summary>
        private SimulatedTrade StepNeutral(Candlestick m1)
        {
            decimal limitPrice = _active.Direction == TradeDirection.Long
                ? _active.SignalPrice - _active.InitialRisk * EntryPullbackFraction
                : _active.SignalPrice + _active.InitialRisk * EntryPullbackFraction;

            bool limitHit = _active.Direction == TradeDirection.Long
                ? m1.Low <= limitPrice
                : m1.High >= limitPrice;
            if (limitHit)
            {
                FillEntry(limitPrice, m1.CloseTime);
                return CheckExit(m1) ? Finish() : null;
            }

            var elapsed = (m1.CloseTime - _active.SignalTime).TotalMinutes;
            if (elapsed >= EntryWindowMinutes)
            {
                FillEntry(m1.Close, m1.CloseTime);
                return CheckExit(m1) ? Finish() : null;
            }
            return null;
        }

        /// <summary>
        /// Range mode: MACD opposing signal but BB bands narrow — a small
        /// counter-push inside a tight range. Wait for price to touch the BB
        /// band on our side (long: BBLower, short: BBUpper) for a discount
        /// entry. Budget 2 × 15M bars; if no touch AND no chase, market-fill
        /// at window expiry so we don't lose the signal entirely.
        /// </summary>
        private SimulatedTrade StepRange(Candlestick m1)
        {
            // Target = BB band on our side. That's the "bottom of the range"
            // for longs and the "top" for shorts.
            decimal target = _active.Direction == TradeDirection.Long
                ? _active.BBLower_AtSignal
                : _active.BBUpper_AtSignal;

            if (target > 0m)
            {
                bool touched = _active.Direction == TradeDirection.Long
                    ? m1.Low <= target
                    : m1.High >= target;
                if (touched)
                {
                    // Fill at the better of the BB band or 1M close to keep
                    // the fill realistic (we wouldn't get a print below low).
                    decimal fill = _active.Direction == TradeDirection.Long
                        ? Math.Max(target, m1.Low)
                        : Math.Min(target, m1.High);
                    FillEntry(fill, m1.CloseTime);
                    return CheckExit(m1) ? Finish() : null;
                }
            }

            // Advance / chase / expiry only at 15M boundaries.
            bool is15MBoundary = m1.CloseTime.Minute % 15 == 0;
            if (!is15MBoundary) return null;

            // Chase: price ran in our favor 0.10R without us → take it.
            decimal chaseLevel = _active.Direction == TradeDirection.Long
                ? _active.SignalPrice + _active.InitialRisk * AdaptiveChaseFrac
                : _active.SignalPrice - _active.InitialRisk * AdaptiveChaseFrac;
            bool priceRanAway = _active.Direction == TradeDirection.Long
                ? m1.Close >= chaseLevel
                : m1.Close <= chaseLevel;
            if (priceRanAway)
            {
                FillEntry(m1.Close, m1.CloseTime);
                return CheckExit(m1) ? Finish() : null;
            }

            // Advance the bar counter; market-fill on expiry rather than
            // cancel — this is a valid signal, just a range we waited in.
            _active.PendingBars++;
            if (_active.PendingBars >= 2)
            {
                FillEntry(m1.Close, m1.CloseTime);
                return CheckExit(m1) ? Finish() : null;
            }
            return null;
        }

        /// <summary>
        /// Bb mode: 1M Bollinger Band (20, 2) entry-timing algorithm. Waits
        /// for 1M price to touch the band on our side of the trade before
        /// filling — the classic "price is oversold / overbought on 1M"
        /// measure the user asked to test.
        ///   LONG  → fill when m1.Low  ≤ lower band (price touched the floor)
        ///   SHORT → fill when m1.High ≥ upper band (price touched the ceiling)
        /// Fill price = better of the band or the bar's wick extreme — we
        /// can't fill beyond the 1M range so we clamp. Budget: 2 × 15M bars
        /// (30 1M bars). If no band touch fires in the window we cancel the
        /// signal. SL-abort guard is handled by the common Step() header.
        /// </summary>
        private SimulatedTrade StepBb(Candlestick m1)
        {
            const int BbMaxMinutes = 30;

            if (Bb1mWarm)
            {
                bool touched = _active.Direction == TradeDirection.Long
                    ? m1.Low  <= _bb1mLower
                    : m1.High >= _bb1mUpper;
                if (touched)
                {
                    decimal fill = _active.Direction == TradeDirection.Long
                        ? Math.Max(_bb1mLower, m1.Low)
                        : Math.Min(_bb1mUpper, m1.High);
                    FillEntry(fill, m1.CloseTime);
                    return CheckExit(m1) ? Finish() : null;
                }
            }

            var elapsed = (m1.CloseTime - _active.SignalTime).TotalMinutes;
            if (elapsed >= BbMaxMinutes)
            {
                _active.EntryTime = m1.CloseTime;
                _active.EntryPrice = m1.Close;
                _active.ExitTime = m1.CloseTime;
                _active.ExitPrice = m1.Close;
                _active.ExitReason = "EntryCancelled_NoBb";
                _active.PnlUsdt = 0m;
                _active.State = TradeState.Closed;
                return Finish();
            }
            return null;
        }

        /// <summary>
        /// BbGuide mode: 1M price-direction entry with BB as a context check.
        /// The BB bands aren't a hard trigger — they're a guide. The real
        /// decision is "is price currently moving in our favor?":
        ///
        ///   LONG  → fill when m1.Close &gt; prev 1M close  (price climbing)
        ///   SHORT → fill when m1.Close &lt; prev 1M close  (price falling)
        ///
        /// If price is moving AGAINST the signal, wait. On every subsequent
        /// 1M bar re-check the direction — fill the first bar that prints a
        /// reversal in our favor. Budget: 2 × 15M bars (30 × 1M). On budget
        /// expiry, market-fill rather than cancel — the user's framing is
        /// "see what it does before placing the trade," not "skip the trade."
        ///
        /// BB context: the bands aren't gated on here but are captured at
        /// signal time in the CSV so post-hoc analysis can show whether fills
        /// clustered near a band extreme. An obvious refinement, once we see
        /// the data: require close &lt; BB upper (long) / close &gt; BB lower
        /// (short) on the fill bar to avoid chasing an already-blown-out move.
        /// </summary>
        private SimulatedTrade StepBbGuide(Candlestick m1, decimal prevClose)
        {
            const int BbGuideMaxMinutes = 30;

            bool aligned = _active.Direction == TradeDirection.Long
                ? m1.Close > prevClose
                : m1.Close < prevClose;

            if (aligned)
            {
                FillEntry(m1.Close, m1.CloseTime);
                return CheckExit(m1) ? Finish() : null;
            }

            var elapsed = (m1.CloseTime - _active.SignalTime).TotalMinutes;
            if (elapsed >= BbGuideMaxMinutes)
            {
                // Budget expired without a favorable turn — the signal is
                // still live, so take it at the current close rather than
                // cancel. This preserves signal coverage; the pullback-wait
                // has already given us the better price when it was available.
                FillEntry(m1.Close, m1.CloseTime);
                return CheckExit(m1) ? Finish() : null;
            }
            return null;
        }

        /// <summary>
        /// SRSI mode: proper 1M entry-timing algorithm. The 15M signal has
        /// fired — we know direction and SL/TP. Instead of market-filling we
        /// wait for Stochastic RSI(14,14,3,3) on 1M closes to confirm a
        /// reasonable entry price:
        ///   LONG  → fill when both %K and %D ≤ 30  (1M oversold)
        ///   SHORT → fill when both %K and %D ≥ 70  (1M overbought)
        /// Budget: 2 × 15M bars (30 × 1M bars) measured from signal time. If
        /// the trigger never prints in that window we cancel the signal —
        /// the idea being that when the 1M isn't giving us a clean extreme,
        /// we're better off skipping than chasing. SL-abort guard still fires
        /// from the common Step() header above this helper.
        /// </summary>
        private SimulatedTrade StepSrsi(Candlestick m1)
        {
            // Cancellation budget is 30 × 1M bars == 2 × 15M = 30 minutes.
            const int SrsiMaxMinutes = 30;

            if (SrsiWarm)
            {
                bool trigger = _active.Direction == TradeDirection.Long
                    ? (_srsiK <= SrsiLongMax  && _srsiD <= SrsiLongMax)
                    : (_srsiK >= SrsiShortMin && _srsiD >= SrsiShortMin);
                if (trigger)
                {
                    FillEntry(m1.Close, m1.CloseTime);
                    return CheckExit(m1) ? Finish() : null;
                }
            }

            var elapsed = (m1.CloseTime - _active.SignalTime).TotalMinutes;
            if (elapsed >= SrsiMaxMinutes)
            {
                _active.EntryTime = m1.CloseTime;
                _active.EntryPrice = m1.Close;
                _active.ExitTime = m1.CloseTime;
                _active.ExitPrice = m1.Close;
                _active.ExitReason = "EntryCancelled_NoSrsi";
                _active.PnlUsdt = 0m;
                _active.State = TradeState.Closed;
                return Finish();
            }
            return null;
        }

        /// <summary>
        /// Late mode: signal fired with price already extended, statistically
        /// a pullback is coming. Wait up to 2 × 15M bars. Within that window,
        /// fill when the 1M RSI(14) prints an exhaustion turn (turn up from
        /// &lt; 40 for long, turn down from &gt; 60 for short) — and price is at
        /// or beyond the deepening limit (0.10R on bar 1, 0.25R on bar 2).
        /// Chase escape fires if price rips away in our favor by 0.10R;
        /// otherwise cancel on budget exhaustion.
        /// </summary>
        private SimulatedTrade StepLate(Candlestick m1)
        {
            int barIdx = Math.Min(_active.PendingBars, 1);   // 0 or 1
            decimal limitFrac = barIdx == 0 ? 0.10m : 0.25m;
            decimal limitPrice = _active.Direction == TradeDirection.Long
                ? _active.SignalPrice - _active.InitialRisk * limitFrac
                : _active.SignalPrice + _active.InitialRisk * limitFrac;

            // 1M RSI exhaustion trigger: require oversold-and-turning-up for
            // longs, overbought-and-turning-down for shorts. Needs at least
            // Rsi1mPeriod + 2 bars of history to produce a meaningful turn.
            bool rsiWarm = _rsi1mBarsSeen > Rsi1mPeriod + 1;
            bool rsiTurn = false;
            if (rsiWarm)
            {
                rsiTurn = _active.Direction == TradeDirection.Long
                    ? (_rsi1mPrev < LateRsiLongMax && _rsi1mCurrent > _rsi1mPrev)
                    : (_rsi1mPrev > LateRsiShortMin && _rsi1mCurrent < _rsi1mPrev);
            }

            // Fill when BOTH: price has reached the pullback limit AND the
            // 1M RSI has turned out of its exhaustion zone. Price check uses
            // the 1M close (not high/low) so the fill aligns with the RSI
            // bar that triggered — entries reflect where we'd actually click.
            bool priceOk = _active.Direction == TradeDirection.Long
                ? m1.Close <= limitPrice
                : m1.Close >= limitPrice;

            if (rsiTurn && priceOk)
            {
                FillEntry(m1.Close, m1.CloseTime);
                return CheckExit(m1) ? Finish() : null;
            }

            // Only advance the bar counter / chase / cancel on 15M boundaries.
            bool is15MBoundary = m1.CloseTime.Minute % 15 == 0;
            if (!is15MBoundary) return null;

            // Chase: price ran AdaptiveChaseFrac × R in our favor without us.
            decimal chaseLevel = _active.Direction == TradeDirection.Long
                ? _active.SignalPrice + _active.InitialRisk * AdaptiveChaseFrac
                : _active.SignalPrice - _active.InitialRisk * AdaptiveChaseFrac;
            bool priceRanAway = _active.Direction == TradeDirection.Long
                ? m1.Close >= chaseLevel
                : m1.Close <= chaseLevel;
            if (priceRanAway)
            {
                FillEntry(m1.Close, m1.CloseTime);
                return CheckExit(m1) ? Finish() : null;
            }

            _active.PendingBars++;
            if (_active.PendingBars >= 2)
            {
                _active.EntryTime = m1.CloseTime;
                _active.EntryPrice = m1.Close;
                _active.ExitTime = m1.CloseTime;
                _active.ExitPrice = m1.Close;
                _active.ExitReason = "EntryCancelled_NoFill";
                _active.PnlUsdt = 0m;
                _active.State = TradeState.Closed;
                return Finish();
            }
            return null;
        }

        /// <summary>
        /// Continuous Wilder RSI(14) on 1M closes. Called on every observed
        /// 1M bar. Seeds avgGain/avgLoss from a simple mean over the first
        /// 14 deltas, then smooths each subsequent bar with the 1/N filter.
        /// </summary>
        private void UpdateRsi1M(decimal close)
        {
            if (_rsi1mBarsSeen == 0)
            {
                _prev1mClose = close;
                _rsi1mBarsSeen = 1;
                return;
            }

            decimal delta = close - _prev1mClose;
            decimal gain = delta > 0 ? delta : 0m;
            decimal loss = delta < 0 ? -delta : 0m;
            _prev1mClose = close;

            if (_rsi1mBarsSeen <= Rsi1mPeriod)
            {
                // Simple running sum during warm-up.
                _rsi1mAvgGain += gain;
                _rsi1mAvgLoss += loss;
                _rsi1mBarsSeen++;
                if (_rsi1mBarsSeen == Rsi1mPeriod + 1)
                {
                    _rsi1mAvgGain /= Rsi1mPeriod;
                    _rsi1mAvgLoss /= Rsi1mPeriod;
                    _rsi1mPrev = _rsi1mCurrent;
                    _rsi1mCurrent = ComputeRsiFromAvg(_rsi1mAvgGain, _rsi1mAvgLoss);
                }
                return;
            }

            // Wilder smoothing: avg = (prev*(N-1) + x) / N
            _rsi1mAvgGain = (_rsi1mAvgGain * (Rsi1mPeriod - 1) + gain) / Rsi1mPeriod;
            _rsi1mAvgLoss = (_rsi1mAvgLoss * (Rsi1mPeriod - 1) + loss) / Rsi1mPeriod;
            _rsi1mBarsSeen++;
            _rsi1mPrev = _rsi1mCurrent;
            _rsi1mCurrent = ComputeRsiFromAvg(_rsi1mAvgGain, _rsi1mAvgLoss);

            UpdateSrsi1M(_rsi1mCurrent);
        }

        /// <summary>
        /// Advance the Stochastic RSI by one new RSI sample. Maintains three
        /// ring buffers: last SrsiStochPeriod RSI values (for min/max), last
        /// SrsiSmoothK stochRSI values (for K SMA), last SrsiSmoothD K values
        /// (for D SMA). Does nothing until each stage has enough history.
        /// </summary>
        private void UpdateSrsi1M(decimal rsi)
        {
            // Stage 1: push RSI into the stoch window.
            _srsiRsiBuf[_srsiRsiIdx] = rsi;
            _srsiRsiIdx = (_srsiRsiIdx + 1) % SrsiStochPeriod;
            if (_srsiRsiCount < SrsiStochPeriod) _srsiRsiCount++;
            if (_srsiRsiCount < SrsiStochPeriod) return;

            decimal lo = _srsiRsiBuf[0], hi = _srsiRsiBuf[0];
            for (int i = 1; i < SrsiStochPeriod; i++)
            {
                decimal v = _srsiRsiBuf[i];
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            decimal range = hi - lo;
            decimal stochRsi = range > 0m ? (rsi - lo) / range * 100m : 50m;

            // Stage 2: K = SMA of last 3 stochRSI.
            _srsiKBuf[_srsiKIdx] = stochRsi;
            _srsiKIdx = (_srsiKIdx + 1) % SrsiSmoothK;
            if (_srsiKCount < SrsiSmoothK) _srsiKCount++;
            if (_srsiKCount < SrsiSmoothK) return;

            decimal kSum = 0m;
            for (int i = 0; i < SrsiSmoothK; i++) kSum += _srsiKBuf[i];
            _srsiK = kSum / SrsiSmoothK;

            // Stage 3: D = SMA of last 3 K.
            _srsiDBuf[_srsiDIdx] = _srsiK;
            _srsiDIdx = (_srsiDIdx + 1) % SrsiSmoothD;
            if (_srsiDCount < SrsiSmoothD) _srsiDCount++;
            if (_srsiDCount < SrsiSmoothD) return;

            decimal dSum = 0m;
            for (int i = 0; i < SrsiSmoothD; i++) dSum += _srsiDBuf[i];
            _srsiD = dSum / SrsiSmoothD;
        }

        private bool SrsiWarm => _srsiDCount >= SrsiSmoothD && _srsiKCount >= SrsiSmoothK && _srsiRsiCount >= SrsiStochPeriod;

        /// <summary>
        /// Advance the 1M Bollinger Bands by one close. Middle = SMA(20),
        /// bands = middle ± 2 × population stddev. Recomputed in-place off
        /// the ring buffer — 20 closes per update is trivially fast.
        /// </summary>
        private void UpdateBb1M(decimal close)
        {
            _bb1mWindow[_bb1mIndex] = close;
            _bb1mIndex = (_bb1mIndex + 1) % Bb1mPeriod;
            if (_bb1mFill < Bb1mPeriod) _bb1mFill++;
            if (_bb1mFill < Bb1mPeriod) return;

            decimal sum = 0m;
            for (int i = 0; i < Bb1mPeriod; i++) sum += _bb1mWindow[i];
            decimal mean = sum / Bb1mPeriod;
            decimal varSum = 0m;
            for (int i = 0; i < Bb1mPeriod; i++)
            {
                decimal d = _bb1mWindow[i] - mean;
                varSum += d * d;
            }
            decimal variance = varSum / Bb1mPeriod;
            decimal stdev = (decimal)Math.Sqrt((double)variance);
            _bb1mMiddle = mean;
            _bb1mUpper = mean + Bb1mSigma * stdev;
            _bb1mLower = mean - Bb1mSigma * stdev;
        }

        private static decimal ComputeRsiFromAvg(decimal avgGain, decimal avgLoss)
        {
            if (avgLoss == 0m) return avgGain == 0m ? 50m : 100m;
            decimal rs = avgGain / avgLoss;
            return 100m - (100m / (1m + rs));
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

            // Update peak MFE (in R) from the running high/low extremes.
            // HighestSinceEntry / LowestSinceEntry already aggregate every 1M
            // bar since entry (including this one), so this covers the full
            // bar-close window we need for the fakeout check below.
            if (t.InitialRisk > 0)
            {
                decimal mfeAbs = t.Direction == TradeDirection.Long
                    ? t.HighestSinceEntry - t.EntryPrice
                    : t.EntryPrice - t.LowestSinceEntry;
                decimal mfeR = mfeAbs / t.InitialRisk;
                if (mfeR > t.MfePeakR) t.MfePeakR = mfeR;
            }

            // Capture bar-1 close direction (the only value we need from it).
            if (t.BarsHeld == 1)
            {
                t.Bar1ClosedAdverse = t.Direction == TradeDirection.Long
                    ? close < t.EntryPrice
                    : close > t.EntryPrice;
            }

            // (a-fakeout) Fakeout exit at bar 2 close. Placed before SL so a
            // weak-but-above-SL trade exits at `close` instead of continuing
            // toward the stop. See EnableFakeoutExit comment for derivation.
            if (EnableFakeoutExit
                && t.BarsHeld == 2
                && t.Bar1ClosedAdverse
                && t.MfePeakR < FakeoutMfeThresholdR)
            {
                CloseAt(close, m1.CloseTime, "FakeoutExit");
                return true;
            }

            // (a0) 30M Structure Break — early exit. DISABLED (see flag).
            // Feature plumbed through SimulatedTrade.Entry30mBarLow/High so
            // variants (e.g. confirmation-gated) can be tested without re-
            // wiring. When enabled, runs before SL so a structure break
            // can exit at a price better than SL.
            if (EnableStructure30mExit && (m1.CloseTime.Minute % 30 == 0))
            {
                if (t.Direction == TradeDirection.Long
                    && t.Entry30mBarLow > 0
                    && close < t.Entry30mBarLow)
                {
                    CloseAt(close, m1.CloseTime, "Structure30m");
                    return true;
                }
                if (t.Direction == TradeDirection.Short
                    && t.Entry30mBarHigh > 0
                    && close > t.Entry30mBarHigh)
                {
                    CloseAt(close, m1.CloseTime, "Structure30m");
                    return true;
                }
            }

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

            // Use the *effective* leverage set at signal time so Opposed trades
            // (2× effective) book PnL consistent with their reduced Quantity.
            // Sizing convention: quantity = equity × effLev / price, and booked
            // PnL = priceMove × quantity × effLev  →  proportional to effLev².
            decimal leverageForPnl = t.EffectiveLeverage > 0m ? t.EffectiveLeverage : t.Leverage;
            t.PnlUsdt = priceMove * t.Quantity * leverageForPnl;
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

            // Update loss-cooldown state. Cancelled signals (any EntryCancelled*
            // reason) are not counted either way — the trade didn't happen so
            // it's neither a win nor a loss. Only real fills count.
            if (EnableLossCooldown && done.ExitReason != null
                && !done.ExitReason.StartsWith("EntryCancelled"))
            {
                if (done.PnlUsdt < 0m)
                {
                    _consecutiveLosses++;
                    if (_consecutiveLosses >= LossCooldownTriggerCount && _remainingSkips == 0)
                        _remainingSkips = LossCooldownSkipCount;
                }
                else if (done.PnlUsdt > 0m)
                {
                    _consecutiveLosses = 0;
                    // Don't clear _remainingSkips here — if we triggered but
                    // haven't burned through the skip budget, finish serving it.
                }
            }

            return done;
        }
    }
}
