using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Linq;

namespace CryptoTrading.App.Algorithm.HtfRsiVolExpansion
{
    /// <summary>
    /// Exit strategy for HTF RSI + Vol Expansion.
    /// Runs on the 1M timeframe (called by TradeMonitor on each 1M candle).
    /// Manages four exit types checked in order:
    ///   a) Stop Loss - hard stop at 1.5 × ATR from entry, moves to breakeven at 1.0R
    ///   b) Dynamic Take Profit - R:R scales with ProbabilityScore:
    ///        Score 80+: no hard TP (trail only)
    ///        Score 60-79: 3.0R
    ///        Score 40-59: 2.0R
    ///        Score &lt;40: 1.5R
    ///   c) Trailing Stop - activates at 1.5R profit, trails at 1.0 × 15M ATR
    ///   d) Time Stop - closes after 240 bars (4 hours on 1M)
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
        private bool _breakevenActive;

        // Parameters (time stop is 4 hours = 240 × 1M bars, since TradeMonitor feeds 1M candles)
        private const int MaxHoldBars = 240;
        private const decimal BreakevenActivationR = 1.0m;
        private const decimal TrailingActivationR = 1.5m;
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
            _breakevenActive = false;
            _trailingStop = _setup?.Direction == TradeDirection.Long
                ? decimal.MinValue
                : decimal.MaxValue;
        }

        public TradeDetails GetNextExit(decimal currentPositionSize, decimal close, decimal profit)
        {
            if (currentPositionSize <= 0 || _quoteHub?.Quotes == null || _quoteHub.Quotes.Count < 15)
                return new TradeDetails { ShouldTrade = false };

            _barsHeld++;

            var lastQuote = _quoteHub.Quotes.Last();
            var high = (decimal)lastQuote.High;
            var low = (decimal)lastQuote.Low;

            // Track price extremes since entry
            if (high > _highestSinceEntry) _highestSinceEntry = high;
            if (low < _lowestSinceEntry) _lowestSinceEntry = low;

            // Trailing stop uses the 15M ATR captured at entry time.
            // The quote hub contains 1M data so computing ATR(14) here would give
            // a 14-minute ATR which is far too small for the strategy's intent.
            var trailingAtr = _setup.AtrAtEntry;

            // a) Stop Loss
            if (_setup.Direction == TradeDirection.Long && low <= _setup.StopLoss)
            {
                RecordExit("StopLoss", _setup.StopLoss, currentPositionSize);
                return MakeExit(currentPositionSize, _setup.StopLoss);
            }
            if (_setup.Direction == TradeDirection.Short && high >= _setup.StopLoss)
            {
                RecordExit("StopLoss", _setup.StopLoss, currentPositionSize);
                return MakeExit(currentPositionSize, _setup.StopLoss);
            }

            // b) Dynamic Take Profit (score-based R:R)
            var tpMultiplier = GetTakeProfitMultiplier(_setup.ProbabilityScore);
            if (tpMultiplier > 0)
            {
                var tpDistance = _setup.InitialRisk * tpMultiplier;
                if (_setup.Direction == TradeDirection.Long && high >= _setup.EntryPrice + tpDistance)
                {
                    var tpPrice = _setup.EntryPrice + tpDistance;
                    RecordExit("TakeProfit", tpPrice, currentPositionSize);
                    return MakeExit(currentPositionSize, tpPrice);
                }
                if (_setup.Direction == TradeDirection.Short && low <= _setup.EntryPrice - tpDistance)
                {
                    var tpPrice = _setup.EntryPrice - tpDistance;
                    RecordExit("TakeProfit", tpPrice, currentPositionSize);
                    return MakeExit(currentPositionSize, tpPrice);
                }
            }

            // c) Breakeven + Trailing Stop
            if (trailingAtr > 0)
            {
                var unrealizedProfit = _setup.Direction == TradeDirection.Long
                    ? close - _setup.EntryPrice
                    : _setup.EntryPrice - close;

                // Move SL to breakeven at 1.0R profit
                if (!_breakevenActive && unrealizedProfit >= _setup.InitialRisk * BreakevenActivationR)
                {
                    _breakevenActive = true;
                    _setup.StopLoss = _setup.EntryPrice;
                    _logger?.LogInformation(
                        $"[EXIT] Breakeven activated — SL moved to entry {_setup.EntryPrice:F2}");
                }

                // Activate trailing stop at 1.5R
                if (unrealizedProfit >= _setup.InitialRisk * TrailingActivationR)
                {
                    if (!_trailingActive)
                    {
                        _trailingActive = true;
                        _logger?.LogInformation(
                            $"[EXIT] Trailing stop activated at {unrealizedProfit:F2} profit (1.5R = {_setup.InitialRisk * TrailingActivationR:F2})");
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
                        if (low <= _trailingStop)
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
                        if (high >= _trailingStop)
                        {
                            RecordExit("TrailingStop", _trailingStop, currentPositionSize);
                            return MakeExit(currentPositionSize, _trailingStop);
                        }
                    }
                }
            }

            // d) Time stop
            if (_barsHeld >= MaxHoldBars)
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
        /// </summary>
        public static decimal GetTakeProfitMultiplier(int probabilityScore)
        {
            return probabilityScore switch
            {
                >= 80 => 0m,    // No hard TP — let trailing stop manage
                >= 60 => 3.0m,  // Strong setup: 3:1 R:R
                >= 40 => 2.0m,  // Moderate setup: 2:1 R:R
                _ => 1.5m       // Weak setup: 1.5:1 R:R
            };
        }

        private void RecordExit(string reason, decimal exitPrice, decimal quantity)
        {
            decimal pnl;
            if (_setup.Direction == TradeDirection.Long)
                pnl = (exitPrice - _setup.EntryPrice) * quantity * _setup.Leverage;
            else
                pnl = (_setup.EntryPrice - exitPrice) * quantity * _setup.Leverage;

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
