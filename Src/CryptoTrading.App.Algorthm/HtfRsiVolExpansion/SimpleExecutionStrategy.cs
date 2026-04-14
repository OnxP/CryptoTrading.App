using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System.Linq;

namespace CryptoTrading.App.Algorithm.HtfRsiVolExpansion
{
    /// <summary>
    /// Simplified execution strategy — enters immediately on setup signal,
    /// exits on hard SL or TP only. No trailing, no breakeven, no time stop.
    ///
    /// The 15M algorithm decides WHEN and WHAT DIRECTION to trade.
    /// This strategy just executes it cleanly:
    ///   - Market entry on first candle
    ///   - SL at 1.5 × ATR from entry
    ///   - TP at 1.5 × ATR from entry (1:1 R:R)
    ///   - That's it.
    /// </summary>
    public class SimpleExecutionStrategy : IExecutionStrategy
    {
        private QuoteHub<IQuote> _quoteHub;
        private readonly HtfRsiVolExpansionSetup _setup;
        private readonly HtfRsiTradingState _tradingState;
        private ILogger _logger;
        private bool _entryPriceAdjusted;
        private bool _setupConsumed;

        public IEntryStrategy EntryStrategy { get; set; }
        public IExitStrategy ExitStrategy { get; set; }
        public decimal Quantity { get; set; }

        public SimpleExecutionStrategy(
            HtfRsiVolExpansionSetup setup,
            HtfRsiTradingState tradingState)
        {
            _setup = setup;
            _tradingState = tradingState;
            Quantity = setup.Quantity;

            EntryStrategy = new HtfRsiVolExpansionEntryStrategy();
            ExitStrategy = new SimpleExitStrategy(setup, tradingState);
        }

        public void SetLogger(ILogger logger)
        {
            _logger = logger;
            if (ExitStrategy is SimpleExitStrategy exit)
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

            // Not in trade — enter immediately
            if (!trade.Open)
            {
                _entryPriceAdjusted = false;

                if (_setupConsumed)
                    return status;

                // Don't enter if SL already breached
                bool slBreached = _setup.Direction == TradeDirection.Long
                    ? currentPrice <= _setup.StopLoss
                    : currentPrice >= _setup.StopLoss;

                if (slBreached)
                    return status;

                var entryDetails = EntryStrategy.GetNextEntry(0, Quantity, currentPrice);
                if (entryDetails.ShouldTrade)
                {
                    _setupConsumed = true;
                    _logger?.LogInformation(
                        $"[SIMPLE ENTRY] {_setup.Direction} | Price:{currentPrice:F2} | Qty:{Quantity:F6} | " +
                        $"SL:{_setup.StopLoss:F2} | TP:{_setup.TakeProfit:F2}");
                    status.StrategyAction = StrategyAction.OpenTrade;
                    status.StrategyState = StrategyState.WaitingForEntry;
                }
                return status;
            }

            // On first candle after fill, rebase SL/TP to actual fill price
            if (!_entryPriceAdjusted)
            {
                _entryPriceAdjusted = true;
                var actualEntry = currentPrice;
                var risk = _setup.InitialRisk;

                _logger?.LogInformation(
                    $"[SIMPLE ADJUST] Rebasing from {_setup.EntryPrice:F2} → {actualEntry:F2}");

                _setup.EntryPrice = actualEntry;
                if (_setup.Direction == TradeDirection.Long)
                {
                    _setup.StopLoss = actualEntry - risk;
                    _setup.TakeProfit = actualEntry + risk;
                }
                else
                {
                    _setup.StopLoss = actualEntry + risk;
                    _setup.TakeProfit = actualEntry - risk;
                }
            }

            // In trade — check SL and TP only
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
