using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System.Linq;

namespace CryptoTrading.App.Algorithm.HtfRsiVolExpansion
{
    /// <summary>
    /// Simplified exit strategy — hard SL and hard TP only.
    /// No trailing stop, no breakeven, no time stop.
    ///
    /// SL = 1.5 × ATR from entry (set by algorithm)
    /// TP = 1.5 × ATR from entry (1:1 R:R, set by algorithm)
    ///
    /// Lets the 15M signal quality speak for itself.
    /// </summary>
    public class SimpleExitStrategy : IExitStrategy
    {
        private QuoteHub<IQuote> _quoteHub;
        private readonly HtfRsiVolExpansionSetup _setup;
        private readonly HtfRsiTradingState _tradingState;
        private ILogger _logger;

        public SimpleExitStrategy(
            HtfRsiVolExpansionSetup setup,
            HtfRsiTradingState tradingState)
        {
            _setup = setup;
            _tradingState = tradingState;
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
            // Nothing to reset — no stateful tracking
        }

        public TradeDetails GetNextExit(decimal currentPositionSize, decimal close, decimal profit)
        {
            if (currentPositionSize <= 0 || _quoteHub?.Quotes == null || _quoteHub.Quotes.Count < 15)
                return new TradeDetails { ShouldTrade = false };

            var lastQuote = _quoteHub.Quotes.Last();
            var high = (decimal)lastQuote.High;
            var low = (decimal)lastQuote.Low;

            // Stop Loss
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

            // Take Profit
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
                $"[SIMPLE EXIT] {reason} | Dir:{_setup.Direction} | Entry:{_setup.EntryPrice:F2} → Exit:{exitPrice:F2} | " +
                $"PnL:{pnl:F2} USDT | Score:{_setup.ProbabilityScore} | " +
                $"4H RSI:{_setup.HtfRsi:F1} | ATR:{_setup.AtrAtEntry:F2} | VolExp:{_setup.VolExpansionRatio:F2}");
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
