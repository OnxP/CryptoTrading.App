using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core.Strategy;
using Skender.Stock.Indicators;
using System;
using System.Linq;

namespace CryptoTrading.App.Algorithm.HtfRsiVolExpansion
{
    /// <summary>
    /// Entry strategy for HTF RSI + Vol Expansion.
    ///
    /// Two modes, selected by constructor:
    ///
    ///   1. Immediate mode (parameterless ctor). The algorithm already
    ///      validated the setup; this strategy just fires a market order on
    ///      the next tick. Legacy pass-through behaviour preserved for
    ///      code paths that don't want the defer.
    ///
    ///   2. BbGuide mode (setup-aware ctor). Defers the fill until the 1M
    ///      tape confirms the trade direction.
    ///        - On every 1M bar seen before fill, compare the current close
    ///          to the previous 1M close. For longs, fire on a rising close
    ///          (close > prev); for shorts, fire on a falling close.
    ///        - Budget: <see cref="BbGuideBudgetMinutes"/> from the signal
    ///          time. On expiry, force-fill at the current 1M close rather
    ///          than cancel — the 15M signal is still live and a late fill
    ///          beats missing the move.
    ///        - SL guard: if the 1M close ever crosses the signal-based SL
    ///          before fill, permanently abort. No point filling into an
    ///          immediate stop-out.
    ///      At fill, the setup's EntryPrice / StopLoss / TakeProfit are
    ///      rebased to the 1M fill price and <see cref="HtfRsiVolExpansionSetup.EntryPriceFinal"/>
    ///      is set so SimpleExecutionStrategy's first-bar rebase is skipped.
    ///
    /// BbGuide mode receives 1M ticks through the shared <see cref="QuoteHub{IQuote}"/>
    /// that the production pipeline wires into both the entry and exit
    /// strategies. The algorithm itself does NOT subscribe to 1M — it fires
    /// the setup immediately on the 15M close and hands the defer decision
    /// over to this strategy. That keeps the 1M data flow on the same rail
    /// that the exit strategy already uses, so BbGuide works identically in
    /// live, paper, and the main-app BackTesting replay.
    /// </summary>
    public class HtfRsiVolExpansionEntryStrategy : IEntryStrategy
    {
        // 30 min == 2 × 15M bars. Matches the original backtest-sim budget.
        private const int BbGuideBudgetMinutes = 30;

        private readonly HtfRsiVolExpansionSetup _setup;
        private readonly bool _bbGuideEnabled;

        private QuoteHub<IQuote> _quoteHub;

        // BbGuide state machine
        private decimal _priorClose1m;
        private bool _priorClose1mSet;
        private DateTime _lastBarSeen;
        private bool _aborted;

        // Per-bar decision cache. The production TradeMonitor calls
        // GetNextEntry twice per 1M tick (once inside
        // SimpleExecutionStrategy.ProcessStrategy to decide whether to
        // return OpenTrade, once inside TradeMonitor.ExecuteEntryStrategy
        // to actually submit the order). Without caching, the second call
        // would re-enter the BbGuide state machine, trip the _lastBarSeen
        // dedupe guard, and return ShouldTrade=false — the order would
        // never be submitted. We evaluate the decision once per distinct
        // bar (including the setup rebase side-effect) and replay the
        // cached TradeDetails for any repeated calls on the same bar.
        private TradeDetails _lastDecision;
        private DateTime _lastDecisionBarTime;

        /// <summary>
        /// Immediate-fill ctor. No defer — the next tick fires a market order.
        /// Kept for back-compat with exec strategies that don't want BbGuide.
        /// </summary>
        public HtfRsiVolExpansionEntryStrategy()
        {
            _setup = null;
            _bbGuideEnabled = false;
        }

        /// <summary>
        /// BbGuide ctor. The strategy defers the fill until the 1M tape
        /// aligns with the setup's direction, the 30-min budget expires, or
        /// the SL is breached during the wait (permanent abort).
        /// </summary>
        public HtfRsiVolExpansionEntryStrategy(HtfRsiVolExpansionSetup setup)
        {
            _setup = setup;
            _bbGuideEnabled = setup != null;
            // Seed the prior-close tracker from the signal price so the very
            // first 1M tick after setup creation has a valid comparator.
            if (setup != null)
            {
                _priorClose1m = setup.EntryPrice;
                _priorClose1mSet = true;
            }
        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            _quoteHub = quoteHub;
        }

        public TradeDetails GetNextEntry(decimal currentPositionSize, decimal targetPositionSize, decimal close)
        {
            // Already at target size - no further entry needed.
            if (currentPositionSize >= targetPositionSize || targetPositionSize <= 0)
                return new TradeDetails { ShouldTrade = false };

            var remaining = targetPositionSize - currentPositionSize;

            // --- Immediate mode (no BbGuide) ---
            if (!_bbGuideEnabled)
            {
                return new TradeDetails
                {
                    ShouldTrade = true,
                    EntryPrice = close,
                    Quantity = remaining,
                    Price = close,
                    OrderType = "MARKET"
                };
            }

            // --- BbGuide mode ---

            // Once we've aborted (SL breached during the wait) we stay dead
            // for the life of this setup.
            if (_aborted)
                return new TradeDetails { ShouldTrade = false };

            if (_quoteHub?.Quotes == null || _quoteHub.Quotes.Count == 0)
                return new TradeDetails { ShouldTrade = false };

            var lastQuote = _quoteHub.Quotes.Last();
            var barTime = lastQuote.Timestamp;

            // Idempotent within a bar — replay the cached decision (and
            // any setup-rebase side effects already applied) for repeated
            // calls on the same bar. The production TradeMonitor calls
            // this twice per tick; we only want the BbGuide state machine
            // to advance once.
            if (_lastDecisionBarTime == barTime && _lastDecision != null)
                return _lastDecision;

            // De-dupe — only act once per distinct bar close. SetQuotes
            // pushes quotes through the hub and ProcessStrategy may be
            // invoked more frequently than bars arrive.
            if (_lastBarSeen == barTime)
                return new TradeDetails { ShouldTrade = false };
            _lastBarSeen = barTime;

            var barClose = (decimal)lastQuote.Close;

            // SL-breach guard. The signal's SL is the pre-fill value pinned
            // on the setup at the 15M close. If the 1M tape has already
            // crossed it, there is no point entering.
            bool slBreached = _setup.Direction == TradeDirection.Long
                ? barClose <= _setup.StopLoss
                : barClose >= _setup.StopLoss;
            if (slBreached)
            {
                _aborted = true;
                return CacheDecision(barTime, new TradeDetails { ShouldTrade = false });
            }

            decimal previousClose = _priorClose1mSet ? _priorClose1m : barClose;
            _priorClose1m = barClose;
            _priorClose1mSet = true;

            // Alignment = this 1M bar moved with the trade direction.
            bool aligned = _setup.Direction == TradeDirection.Long
                ? barClose > previousClose
                : barClose < previousClose;

            var elapsed = (barTime - _setup.EntryTime).TotalMinutes;
            bool budgetExpired = elapsed >= BbGuideBudgetMinutes;

            if (!aligned && !budgetExpired)
                return CacheDecision(barTime, new TradeDetails { ShouldTrade = false });

            // Fire. Rebase the setup's prices to the 1M fill and mark the
            // setup final so SimpleExecutionStrategy skips its rebase.
            var risk = _setup.InitialRisk;
            _setup.EntryPrice = barClose;
            if (_setup.Direction == TradeDirection.Long)
            {
                _setup.StopLoss = barClose - risk;
                _setup.TakeProfit = barClose + risk;
            }
            else
            {
                _setup.StopLoss = barClose + risk;
                _setup.TakeProfit = barClose - risk;
            }
            _setup.EntryTime = barTime;
            _setup.EntryPriceFinal = true;

            return CacheDecision(barTime, new TradeDetails
            {
                ShouldTrade = true,
                EntryPrice = barClose,
                Quantity = remaining,
                Price = barClose,
                OrderType = "MARKET"
            });
        }

        private TradeDetails CacheDecision(DateTime barTime, TradeDetails decision)
        {
            _lastDecisionBarTime = barTime;
            _lastDecision = decision;
            return decision;
        }
    }
}
