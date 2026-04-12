using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System.Linq;

namespace CryptoTrading.App.Algorithm.HtfRsiVolExpansion
{
    /// <summary>
    /// Execution strategy for HTF RSI + Vol Expansion.
    /// Implements IExecutionStrategy to integrate with TradeMonitor.
    /// Bridges the entry strategy (immediate market entry) and exit strategy
    /// (SL/TP/trailing/time stop).
    /// </summary>
    public class HtfRsiVolExpansionExecutionStrategy : IExecutionStrategy
    {
        private QuoteHub<IQuote> _quoteHub;
        private readonly HtfRsiVolExpansionSetup _setup;
        private ILogger _logger;

        public IEntryStrategy EntryStrategy { get; set; }
        public IExitStrategy ExitStrategy { get; set; }
        public decimal Quantity { get; set; }

        public HtfRsiVolExpansionExecutionStrategy(
            HtfRsiVolExpansionSetup setup,
            HtfRsiTradingState tradingState)
        {
            _setup = setup;
            Quantity = setup.Quantity;

            EntryStrategy = new HtfRsiVolExpansionEntryStrategy();
            ExitStrategy = new HtfRsiVolExpansionExitStrategy(setup, tradingState);
        }

        public void SetLogger(ILogger logger)
        {
            _logger = logger;
            if (ExitStrategy is HtfRsiVolExpansionExitStrategy exit)
                exit.SetLogger(logger);
        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            _quoteHub = quoteHub;
            EntryStrategy?.SetQuotes(quoteHub);
            ExitStrategy?.SetQuotes(quoteHub);
        }

        public decimal GetEntryPrice()
        {
            return _setup?.EntryPrice ?? 0;
        }

        public StrategyStatus ProcessStrategy(ITrade trade)
        {
            var status = new StrategyStatus
            {
                StrategyAction = StrategyAction.NoAction,
                StrategyState = StrategyState.WaitingForEntry
            };

            if (_quoteHub?.Quotes == null || _quoteHub.Quotes.Count < 15)
                return status;

            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;

            // Not in trade - enter immediately
            if (!trade.Open)
            {
                var entryDetails = EntryStrategy.GetNextEntry(0, Quantity, currentPrice);
                if (entryDetails.ShouldTrade)
                {
                    _logger?.LogInformation(
                        $"[ENTRY] {_setup.Direction} | Price:{currentPrice:F2} | Qty:{Quantity:F6} | " +
                        $"SL:{_setup.StopLoss:F2} | TP:{_setup.TakeProfit:F2} | " +
                        $"Score:{_setup.ProbabilityScore} | 4H RSI:{_setup.HtfRsi:F1}");
                    status.StrategyAction = StrategyAction.OpenTrade;
                    status.StrategyState = StrategyState.WaitingForEntry;
                }
                return status;
            }

            // In trade - check exit conditions
            status.StrategyState = StrategyState.WaitingForExit;
            var exitDetails = ExitStrategy.GetNextExit(
                trade.TotalOpenBaseQuantity, currentPrice, trade.ProfitPct);

            if (exitDetails.ShouldTrade)
            {
                status.StrategyAction = StrategyAction.CloseTrade;
                status.StrategyState = StrategyState.ExitSubmitted;
                status.ExitDetails = exitDetails;
            }

            return status;
        }
    }
}
