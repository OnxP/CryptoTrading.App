using CryptoTrading.App.Algorithm.RegimeBased;
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
        private readonly HtfRsiTradingState _tradingState;
        private ILogger _logger;
        private bool _entryPriceAdjusted;
        private bool _setupConsumed;

        public IEntryStrategy EntryStrategy { get; set; }
        public IExitStrategy ExitStrategy { get; set; }
        public decimal Quantity { get; set; }

        public HtfRsiVolExpansionExecutionStrategy(
            HtfRsiVolExpansionSetup setup,
            HtfRsiTradingState tradingState)
        {
            _setup = setup;
            _tradingState = tradingState;
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

            // Not in trade - enter if setup SL/TP haven't been breached by current price
            if (!trade.Open)
            {
                _entryPriceAdjusted = false;

                // Each setup can only fire ONE trade. After that trade completes,
                // the algorithm must provide a fresh setup via SetNewRequest.
                if (_setupConsumed)
                    return status;

                // Enforce gap and cooldown from HtfRsiTradingState
                if (!_tradingState.CanTrade)
                    return status;

                // Don't re-enter with a stale setup whose SL/TP no longer make sense
                bool slBreached = _setup.Direction == TradeDirection.Long
                    ? currentPrice <= _setup.StopLoss
                    : currentPrice >= _setup.StopLoss;
                bool tpBreached = _setup.Direction == TradeDirection.Long
                    ? currentPrice >= _setup.TakeProfit
                    : currentPrice <= _setup.TakeProfit;

                if (slBreached || tpBreached)
                    return status; // setup is stale, wait for fresh one

                var entryDetails = EntryStrategy.GetNextEntry(0, Quantity, currentPrice);
                if (entryDetails.ShouldTrade)
                {
                    _setupConsumed = true;
                    _tradingState.IsInPosition = true;
                    _logger?.LogInformation(
                        $"[ENTRY] {_setup.Direction} | Price:{currentPrice:F2} | Qty:{Quantity:F6} | " +
                        $"SL:{_setup.StopLoss:F2} | TP:{_setup.TakeProfit:F2} | " +
                        $"Score:{_setup.ProbabilityScore} | 4H RSI:{_setup.HtfRsi:F1}");
                    status.StrategyAction = StrategyAction.OpenTrade;
                    status.StrategyState = StrategyState.WaitingForEntry;
                }
                return status;
            }

            // On first candle after trade opens, recalculate SL/TP from actual fill price
            if (!_entryPriceAdjusted)
            {
                _entryPriceAdjusted = true;
                var actualEntry = currentPrice;
                var risk = _setup.InitialRisk;

                _logger?.LogInformation(
                    $"[ADJUST] Rebasing SL/TP from setup entry {_setup.EntryPrice:F2} → actual {actualEntry:F2}");

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

                // Reset exit strategy tracking for the new price levels
                ExitStrategy?.ResetForNewTrade();
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
