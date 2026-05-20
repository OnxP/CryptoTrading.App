using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Linq;

namespace CryptoTrading.App.Monitor.Strategies.HtfRsiVolExpansion
{
    /// <summary>
    /// Exit strategy for HTF RSI + Vol Expansion.
    ///
    /// The 15M algorithm is authoritative for exit decisions. Between 15M
    /// boundaries this strategy returns NoTrade and only accumulates rolling
    /// high/low so the trail level (when activated at the next 15M close)
    /// sees the full interim range. On a 15M boundary it evaluates the full
    /// exit ladder against the 15M close:
    ///   a) Stop Loss — close beyond 1.5 × ATR SL
    ///   b) Dynamic Take Profit — R:R scales with ProbabilityScore:
    ///        Score 80+: no hard TP (trail only)
    ///        Score 60-79: 2.0R
    ///        Score 40-59: 1.5R
    ///        Score &lt;40: 1.0R
    ///   c) Trailing Stop — activates at 1.0R profit, trails at 1.0 × 15M ATR
    ///   d) Time Stop — closes after 16 × 15M bars (4 hours)
    /// Note: SL and trailing distance use 15M ATR from the setup,
    /// not 1M ATR from the execution quote hub.
    /// </summary>
    public class HtfRsiVolExpansionExitStrategy : IExitStrategy
    {
        private QuoteHub<IQuote> _quoteHub;
        private readonly HtfRsiVolExpansionSetup _setup;
        private readonly HtfRsiTradingState _tradingState;
        private ILogger _logger;

        // Position tracking
        private int _barsHeld;
        private decimal _highestSinceEntry;
        private decimal _lowestSinceEntry;

        // Trailing stop state
        private bool _trailingActive;
        private decimal _trailingStop;

        // Parameters.
        // BarsHeld counts 15M boundaries (not 1M ticks) because exit decisions
        // only fire on 15M closes — 4 hours == 16 × 15M bars.
        private const int MaxHoldBars15M = 16;
        private const decimal TrailingActivationR = 1.0m;
        private const decimal TrailingDistanceAtrMult = 1.0m;

        public HtfRsiVolExpansionExitStrategy(
            HtfRsiVolExpansionSetup setup,
            HtfRsiTradingState tradingState)
        {
            _setup = setup;
            _tradingState = tradingState;
            ResetForNewTrade();
        }

        public void SetLogger(ILogger logger)
        {
            _logger = logger;
        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            _quoteHub = quoteHub;
        }

        public void ResetForNewTrade()
        {
            _barsHeld = 0;
            _highestSinceEntry = _setup?.EntryPrice ?? 0;
            _lowestSinceEntry = _setup?.EntryPrice ?? decimal.MaxValue;
            _trailingActive = false;
            _trailingStop = _setup?.Direction == TradeDirection.Long
                ? decimal.MinValue
                : decimal.MaxValue;
        }

        public TradeDetails GetNextExit(decimal currentPositionSize, decimal close, decimal profit)
        {
            if (currentPositionSize <= 0 || _quoteHub?.Quotes == null || _quoteHub.Quotes.Count < 15)
                return new TradeDetails { ShouldTrade = false };

            var lastQuote = _quoteHub.Quotes.Last();
            var high = (decimal)lastQuote.High;
            var low = (decimal)lastQuote.Low;

            // Always accumulate rolling high/low so the trailing stop sees the
            // full interim range when it activates at the next 15M close.
            if (high > _highestSinceEntry) _highestSinceEntry = high;
            if (low < _lowestSinceEntry) _lowestSinceEntry = low;

            // Exit decisions are gated to 15M bar closes. Between boundaries
            // return NoTrade — the 1M layer keeps the position untouched.
            var closeTime = lastQuote.Timestamp;
            bool is15MBoundary = closeTime.Minute % 15 == 0;
            if (!is15MBoundary)
                return new TradeDetails { ShouldTrade = false };

            _barsHeld++;

            // Trailing stop uses the 15M ATR captured at entry time.
            // The quote hub contains 1M data so computing ATR(14) here would give
            // a 14-minute ATR which is far too small for the strategy's intent.
            var trailingAtr = _setup.AtrAtEntry;

            // a) Stop Loss — 15M close beyond SL
            if (_setup.Direction == TradeDirection.Long && close <= _setup.StopLoss)
            {
                RecordExit("StopLoss", _setup.StopLoss, currentPositionSize);
                return MakeExit(currentPositionSize, _setup.StopLoss);
            }
            if (_setup.Direction == TradeDirection.Short && close >= _setup.StopLoss)
            {
                RecordExit("StopLoss", _setup.StopLoss, currentPositionSize);
                return MakeExit(currentPositionSize, _setup.StopLoss);
            }

            // b) Dynamic Take Profit (score-based R:R) — evaluated on 15M close.
            var tpMultiplier = GetTakeProfitMultiplier(_setup.ProbabilityScore);
            if (tpMultiplier > 0)
            {
                var tpDistance = _setup.InitialRisk * tpMultiplier;
                if (_setup.Direction == TradeDirection.Long && close >= _setup.EntryPrice + tpDistance)
                {
                    var tpPrice = _setup.EntryPrice + tpDistance;
                    RecordExit("TakeProfit", tpPrice, currentPositionSize);
                    return MakeExit(currentPositionSize, tpPrice);
                }
                if (_setup.Direction == TradeDirection.Short && close <= _setup.EntryPrice - tpDistance)
                {
                    var tpPrice = _setup.EntryPrice - tpDistance;
                    RecordExit("TakeProfit", tpPrice, currentPositionSize);
                    return MakeExit(currentPositionSize, tpPrice);
                }
            }

            // c) Trailing Stop. Activates at 1.0R profit (measured on 15M close)
            // and trails at 1.0 × ATR behind the rolling extreme. At activation
            // (1R), the trail sits ~0.33R in profit, giving the trade room to
            // breathe through normal volatility while still protecting gains.
            if (trailingAtr > 0)
            {
                var unrealizedProfit = _setup.Direction == TradeDirection.Long
                    ? close - _setup.EntryPrice
                    : _setup.EntryPrice - close;

                // Activate trailing stop at 1.0R
                if (unrealizedProfit >= _setup.InitialRisk * TrailingActivationR)
                {
                    if (!_trailingActive)
                    {
                        _trailingActive = true;
                        _logger?.LogInformation(
                            $"[EXIT] Trailing stop activated at {unrealizedProfit:F2} profit (1.0R = {_setup.InitialRisk * TrailingActivationR:F2})");
                    }
                }

                if (_trailingActive)
                {
                    var trailDistance = trailingAtr * TrailingDistanceAtrMult;

                    if (_setup.Direction == TradeDirection.Long)
                    {
                        var newTrail = _highestSinceEntry - trailDistance;
                        if (newTrail > _trailingStop)
                            _trailingStop = newTrail;
                        if (close <= _trailingStop)
                        {
                            RecordExit("TrailingStop", _trailingStop, currentPositionSize);
                            return MakeExit(currentPositionSize, _trailingStop);
                        }
                    }
                    else
                    {
                        var newTrail = _lowestSinceEntry + trailDistance;
                        if (newTrail < _trailingStop)
                            _trailingStop = newTrail;
                        if (close >= _trailingStop)
                        {
                            RecordExit("TrailingStop", _trailingStop, currentPositionSize);
                            return MakeExit(currentPositionSize, _trailingStop);
                        }
                    }
                }
            }

            // d) Time stop — 16 × 15M bars (4 hours)
            if (_barsHeld >= MaxHoldBars15M)
            {
                RecordExit("TimeStop", close, currentPositionSize);
                return MakeExit(currentPositionSize, close);
            }

            return new TradeDetails { ShouldTrade = false };
        }

        /// <summary>
        /// Returns the R-multiple for take profit based on the setup's probability score.
        /// Higher-conviction setups get wider targets to let winners run.
        /// Returns 0 for scores ≥ 80 (no hard TP — trailing stop only).
        /// Multipliers are calibrated so that TP is reachable within the 4-hour
        /// time stop window without being prematurely clipped by the trailing stop.
        /// </summary>
        public static decimal GetTakeProfitMultiplier(int probabilityScore)
        {
            return probabilityScore switch
            {
                >= 80 => 0m,    // No hard TP — let trailing stop manage
                >= 60 => 2.0m,  // Strong setup: 2:1 R:R (3.0 ATR target)
                >= 40 => 1.5m,  // Moderate setup: 1.5:1 R:R (2.25 ATR target)
                _ => 1.0m       // Weak setup: 1:1 R:R (1.5 ATR target)
            };
        }

        private void RecordExit(string reason, decimal exitPrice, decimal quantity)
        {
            // Perp-style realised PnL: priceDelta × quantity. Quantity already
            // encodes leverage at sizing time (notional = equity × leverage,
            // qty = notional / entryPrice — see HtfRsiPositionSizer), so an
            // extra "× Leverage" here would double-count and inflate every
            // exit by the leverage factor.
            decimal pnl;
            if (_setup.Direction == TradeDirection.Long)
                pnl = (exitPrice - _setup.EntryPrice) * quantity;
            else
                pnl = (_setup.EntryPrice - exitPrice) * quantity;

            _tradingState.RecordTradeComplete(reason, pnl);

            _logger?.LogInformation(
                $"[EXIT] {reason} | Dir:{_setup.Direction} | Entry:{_setup.EntryPrice:F2} → Exit:{exitPrice:F2} | " +
                $"Bars:{_barsHeld} | PnL:{pnl:F2} USDT | Score:{_setup.ProbabilityScore} | " +
                $"4H RSI:{_setup.HtfRsi:F1} | ATR:{_setup.AtrAtEntry:F2} | VolExp:{_setup.VolExpansionRatio:F2} | " +
                $"Trail:{(_trailingActive ? $"{_trailingStop:F2}" : "inactive")}");
        }

        private static TradeDetails MakeExit(decimal quantity, decimal price)
        {
            return new TradeDetails
            {
                ShouldTrade = true,
                Quantity = quantity,
                Price = price,
                EntryPrice = price,
                OrderType = "MARKET"
            };
        }
    }
}
