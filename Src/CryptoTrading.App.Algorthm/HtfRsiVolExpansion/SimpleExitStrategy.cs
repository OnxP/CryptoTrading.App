using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System.Linq;

namespace CryptoTrading.App.Algorithm.HtfRsiVolExpansion
{
    /// <summary>
    /// Exit strategy matching the backtested spec exactly.
    /// Checked on each 1M candle (TradeMonitor feeds 1M data).
    ///
    /// Exit priority:
    ///   a) Stop Loss  — 1.5 × ATR from entry (candle low/high)
    ///   b) Take Profit — 1.5 × ATR from entry, 1:1 R:R (candle high/low)
    ///   c) Trailing Stop — activates at 1.5R profit, trails at 1.0 × ATR
    ///   d) Time Stop  — 16 × 15M candles = 240 × 1M bars (4 hours)
    ///
    /// Probability score is logged only — does not affect TP or sizing.
    /// </summary>
    public class SimpleExitStrategy : IExitStrategy
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

        // Parameters
        private const int MaxHoldBars = 240;           // 16 × 15M = 240 × 1M
        private const decimal TrailingActivationR = 1.5m; // Activate at 1.5R profit
        private const decimal TrailingDistanceAtrMult = 1.0m; // Trail at 1.0 × ATR

        public SimpleExitStrategy(
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

            _barsHeld++;

            var lastQuote = _quoteHub.Quotes.Last();
            var high = (decimal)lastQuote.High;
            var low = (decimal)lastQuote.Low;

            // Track price extremes since entry
            if (high > _highestSinceEntry) _highestSinceEntry = high;
            if (low < _lowestSinceEntry) _lowestSinceEntry = low;

            // Use 15M ATR captured at entry (the quote hub has 1M data,
            // so computing ATR here would give 14-minute ATR, not 15M ATR)
            var atr = _setup.AtrAtEntry;

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

            // c) Trailing Stop — check activation first so we know whether to use TP or trail.
            //    Activates at 1.5R profit (based on close), trails at 1.0 × ATR.
            //    Once trailing is active, TP is superseded — the trail manages the exit.
            if (atr > 0)
            {
                var unrealizedProfit = _setup.Direction == TradeDirection.Long
                    ? close - _setup.EntryPrice
                    : _setup.EntryPrice - close;

                if (unrealizedProfit >= _setup.InitialRisk * TrailingActivationR)
                {
                    if (!_trailingActive)
                    {
                        _trailingActive = true;
                        _logger?.LogInformation(
                            $"[EXIT] Trailing stop activated at {unrealizedProfit:F2} profit " +
                            $"(1.5R = {_setup.InitialRisk * TrailingActivationR:F2})");
                    }
                }

                // b) Take Profit — only when trailing is NOT active.
                //    Once trailing activates it supersedes TP, letting winners run.
                if (!_trailingActive)
                {
                    if (_setup.Direction == TradeDirection.Long && high >= _setup.TakeProfit)
                    {
                        RecordExit("TakeProfit", _setup.TakeProfit, currentPositionSize);
                        return MakeExit(currentPositionSize, _setup.TakeProfit);
                    }
                    if (_setup.Direction == TradeDirection.Short && low <= _setup.TakeProfit)
                    {
                        RecordExit("TakeProfit", _setup.TakeProfit, currentPositionSize);
                        return MakeExit(currentPositionSize, _setup.TakeProfit);
                    }
                }

                if (_trailingActive)
                {
                    var trailDistance = atr * TrailingDistanceAtrMult;

                    if (_setup.Direction == TradeDirection.Long)
                    {
                        // Trail = highest high - ATR, only moves UP
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
                        // Trail = lowest low + ATR, only moves DOWN
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

            // d) Time Stop — 16 × 15M candles = 240 × 1M bars
            if (_barsHeld >= MaxHoldBars)
            {
                RecordExit("TimeStop", close, currentPositionSize);
                return MakeExit(currentPositionSize, close);
            }

            return new TradeDetails { ShouldTrade = false };
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
