using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Linq;

namespace CryptoTrading.App.Algorithm.DualRegime
{
    public sealed class DualRegimeSetup
    {
        public string StrategyType { get; set; } // "trend" or "chop"
        public string SignalType { get; set; }    // "bull_bull", "chop", etc.
        public TradeDirection Direction { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal StopLoss { get; set; }
        public decimal TakeProfit { get; set; }
        public decimal Quantity { get; set; }
        public int Leverage { get; set; }
        public double StopPct { get; set; }
        public double TargetPct { get; set; }
        public DateTime EntryTime { get; set; }

        // Trailing stop state
        public decimal PeakPrice { get; set; }
        public double Mfe { get; set; }
        public double Mae { get; set; }
        public int BarsHeld { get; set; }
        public bool BeEngaged { get; set; }
        public bool TrailEngaged { get; set; }

        // For entry price rebase
        public bool EntryPriceFinal { get; set; }
    }

    public sealed class DualRegimeStrategyResult : StrategyResult
    {
        public DualRegimeSetup Setup { get; set; }
    }

    /// <summary>
    /// Immediate market-order entry strategy for DualRegime.
    /// Returns the full target quantity as a market order on the first call.
    /// </summary>
    internal sealed class ImmediateEntryStrategy : IEntryStrategy
    {
        public TradeDetails GetNextEntry(decimal currentPositionSize, decimal targetPositionSize, decimal close)
        {
            decimal remaining = targetPositionSize - currentPositionSize;
            if (remaining <= 0)
                return new TradeDetails { ShouldTrade = false };

            return new TradeDetails
            {
                ShouldTrade = true,
                Quantity = remaining,
                Price = close,
                EntryPrice = close,
                OrderType = "MARKET"
            };
        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub) { }
    }

    public sealed class DualRegimeExecutionStrategy : IExecutionStrategy
    {
        private readonly DualRegimeSetup _setup;
        private readonly DualRegimeParams _params;
        private readonly DualRegimeTradingState _tradingState;
        private QuoteHub<IQuote> _quoteHub;
        private ILogger _logger;
        private bool _entryPriceAdjusted;
        private bool _setupConsumed;

        public IEntryStrategy EntryStrategy { get; set; }
        public IExitStrategy ExitStrategy { get; set; }
        public decimal Quantity { get; set; }

        public DualRegimeExecutionStrategy(
            DualRegimeSetup setup,
            DualRegimeParams parameters,
            DualRegimeTradingState tradingState)
        {
            _setup = setup;
            _params = parameters;
            _tradingState = tradingState;
            Quantity = setup.Quantity;
            EntryStrategy = new ImmediateEntryStrategy();
        }

        public void SetLogger(ILogger logger) => _logger = logger;

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            _quoteHub = quoteHub;
            EntryStrategy?.SetQuotes(quoteHub);
            ExitStrategy?.SetQuotes(quoteHub);
        }

        public decimal GetEntryPrice() => _setup?.EntryPrice ?? 0;

        public StrategyStatus ProcessStrategy(ITrade trade)
        {
            var status = new StrategyStatus
            {
                StrategyAction = StrategyAction.NoAction,
                StrategyState = StrategyState.WaitingForEntry
            };

            if (_quoteHub?.Quotes == null || _quoteHub.Quotes.Count < 5)
                return status;

            var currentQuote = _quoteHub.Quotes.Last();
            var currentPrice = (decimal)currentQuote.Close;
            var barHigh = (decimal)currentQuote.High;
            var barLow = (decimal)currentQuote.Low;

            if (!trade.Open)
            {
                if (_setupConsumed) return status;

                bool slBreached = _setup.Direction == TradeDirection.Long
                    ? currentPrice <= _setup.StopLoss
                    : currentPrice >= _setup.StopLoss;

                if (slBreached) return status;

                _setupConsumed = true;
                _logger?.LogInformation(
                    $"[DUAL-REGIME ENTRY] {_setup.StrategyType}/{_setup.SignalType} {_setup.Direction} | " +
                    $"Price:{currentPrice:F2} | Qty:{Quantity:F6} | " +
                    $"SL:{_setup.StopLoss:F2} | TP:{_setup.TakeProfit:F2}");

                status.StrategyAction = StrategyAction.OpenTrade;
                return status;
            }

            // Rebase SL/TP to actual fill price on first bar after fill
            if (!_entryPriceAdjusted)
            {
                _entryPriceAdjusted = true;
                if (!_setup.EntryPriceFinal)
                    RebaseToFillPrice(currentPrice);
            }

            // ── Position management ──
            _setup.BarsHeld++;
            bool isLong = _setup.Direction == TradeDirection.Long;

            // Update MFE/MAE
            if (isLong)
            {
                double maeNow = (double)(barLow - _setup.EntryPrice) / (double)_setup.EntryPrice;
                _setup.Mae = Math.Min(_setup.Mae, maeNow);
                decimal newPeak = Math.Max(_setup.PeakPrice, barHigh);
                _setup.PeakPrice = newPeak;
                _setup.Mfe = Math.Max(_setup.Mfe, (double)(newPeak - _setup.EntryPrice) / (double)_setup.EntryPrice);
            }
            else
            {
                double maeNow = (double)(_setup.EntryPrice - barHigh) / (double)_setup.EntryPrice;
                _setup.Mae = Math.Min(_setup.Mae, maeNow);
                decimal newTrough = Math.Min(_setup.PeakPrice, barLow);
                _setup.PeakPrice = newTrough;
                _setup.Mfe = Math.Max(_setup.Mfe, (double)(_setup.EntryPrice - newTrough) / (double)_setup.EntryPrice);
            }

            // 1. Check stop at PRE-bar level (conservative)
            bool stopHit = isLong
                ? barLow <= _setup.StopLoss
                : barHigh >= _setup.StopLoss;

            if (stopHit)
                return ClosePosition(status, _setup.StopLoss, "stop");

            // 2. Update trailing stop
            if (_setup.StrategyType == "trend")
                UpdateTrendTrail();
            else
                UpdateChopTrail();

            // 3. Check target (trend only)
            if (_setup.StrategyType == "trend" && _setup.TakeProfit > 0)
            {
                bool targetHit = isLong
                    ? barHigh >= _setup.TakeProfit
                    : barLow <= _setup.TakeProfit;
                if (targetHit)
                    return ClosePosition(status, _setup.TakeProfit, "target");
            }

            // 4. Max hold (time-based: MaxHoldBars hours)
            // ProcessStrategy is called per 1M candle by TradeMonitor, so use
            // elapsed time rather than bar count for the hold limit.
            var holdDuration = currentQuote.Timestamp - _setup.EntryTime;
            if (holdDuration.TotalHours >= _params.MaxHoldBars)
            {
                decimal slip = (decimal)_params.Slippage;
                decimal exitPrice = isLong
                    ? currentPrice * (1 - slip)
                    : currentPrice * (1 + slip);
                return ClosePosition(status, exitPrice, "max_hold");
            }

            status.StrategyState = StrategyState.WaitingForExit;
            return status;
        }

        private void UpdateTrendTrail()
        {
            double mfe = _setup.Mfe;
            bool isLong = _setup.Direction == TradeDirection.Long;

            if (isLong)
            {
                decimal beLevel = _setup.EntryPrice * (1 + (decimal)_params.TrendBeOffset);
                if (mfe >= _params.TrendTrailTrigger)
                {
                    decimal lockPct = (decimal)(mfe * _params.TrendTrailLockFraction);
                    decimal newStop = _setup.EntryPrice * (1 + lockPct);
                    if (newStop > _setup.StopLoss) _setup.StopLoss = newStop;
                }
                else if (mfe >= _params.TrendBeTrigger)
                {
                    if (beLevel > _setup.StopLoss) _setup.StopLoss = beLevel;
                }
            }
            else
            {
                decimal beLevel = _setup.EntryPrice * (1 - (decimal)_params.TrendBeOffset);
                if (mfe >= _params.TrendTrailTrigger)
                {
                    decimal lockPct = (decimal)(mfe * _params.TrendTrailLockFraction);
                    decimal newStop = _setup.EntryPrice * (1 - lockPct);
                    if (newStop < _setup.StopLoss) _setup.StopLoss = newStop;
                }
                else if (mfe >= _params.TrendBeTrigger)
                {
                    if (beLevel < _setup.StopLoss) _setup.StopLoss = beLevel;
                }
            }
        }

        private void UpdateChopTrail()
        {
            double mfe = _setup.Mfe;
            bool isLong = _setup.Direction == TradeDirection.Long;

            // BE engages once at MFE >= be_trigger
            if (mfe >= _params.ChopBeTrigger && !_setup.BeEngaged)
            {
                if (isLong)
                {
                    decimal beStop = _setup.EntryPrice * (1 + (decimal)_params.ChopBeOffset);
                    if (beStop > _setup.StopLoss) _setup.StopLoss = beStop;
                }
                else
                {
                    decimal beStop = _setup.EntryPrice * (1 - (decimal)_params.ChopBeOffset);
                    if (beStop < _setup.StopLoss) _setup.StopLoss = beStop;
                }
                _setup.BeEngaged = true;
            }

            // After BE engaged, ratchet stop with MFE
            if (_setup.BeEngaged)
            {
                decimal lockPct = (decimal)(_params.ChopLockFraction * mfe);
                if (isLong)
                {
                    decimal ratchet = _setup.EntryPrice * (1 + lockPct);
                    if (ratchet > _setup.StopLoss)
                    {
                        _setup.StopLoss = ratchet;
                        _setup.TrailEngaged = true;
                    }
                }
                else
                {
                    decimal ratchet = _setup.EntryPrice * (1 - lockPct);
                    if (ratchet < _setup.StopLoss)
                    {
                        _setup.StopLoss = ratchet;
                        _setup.TrailEngaged = true;
                    }
                }
            }
        }

        private StrategyStatus ClosePosition(StrategyStatus status, decimal exitPrice, string reason)
        {
            bool isLong = _setup.Direction == TradeDirection.Long;
            double grossRet = isLong
                ? (double)(exitPrice - _setup.EntryPrice) / (double)_setup.EntryPrice
                : (double)(_setup.EntryPrice - exitPrice) / (double)_setup.EntryPrice;

            _tradingState.IsInPosition = false;

            _logger?.LogInformation(
                $"[DUAL-REGIME EXIT] {_setup.StrategyType}/{_setup.SignalType} {reason} | " +
                $"Gross:{grossRet * 100:F3}% | MFE:{_setup.Mfe * 100:F3}% | " +
                $"MAE:{_setup.Mae * 100:F3}% | Bars:{_setup.BarsHeld}");

            status.StrategyAction = StrategyAction.CloseTrade;
            status.StrategyState = StrategyState.ExitSubmitted;
            status.ExitDetails = new TradeDetails
            {
                ShouldTrade = true,
                Price = exitPrice,
            };
            return status;
        }

        private void RebaseToFillPrice(decimal actualFill)
        {
            bool isLong = _setup.Direction == TradeDirection.Long;
            decimal riskAmount = Math.Abs(_setup.EntryPrice - _setup.StopLoss);

            _logger?.LogInformation(
                $"[DUAL-REGIME REBASE] {_setup.EntryPrice:F2} -> {actualFill:F2}");

            _setup.EntryPrice = actualFill;
            _setup.PeakPrice = actualFill;

            if (_setup.StrategyType == "trend")
            {
                if (isLong)
                {
                    _setup.StopLoss = actualFill * (1 - (decimal)_setup.StopPct);
                    _setup.TakeProfit = actualFill * (1 + (decimal)_setup.TargetPct);
                }
                else
                {
                    _setup.StopLoss = actualFill * (1 + (decimal)_setup.StopPct);
                    _setup.TakeProfit = actualFill * (1 - (decimal)_setup.TargetPct);
                }
            }
            else
            {
                if (isLong)
                    _setup.StopLoss = actualFill * (1 - (decimal)_setup.StopPct);
                else
                    _setup.StopLoss = actualFill * (1 + (decimal)_setup.StopPct);
                _setup.TakeProfit = 0; // chop has no fixed target
            }
        }
    }
}
