using CryptoTrading.App.Algorithm.RegimeBased.EntryStrategies;
using CryptoTrading.App.Algorithm.RegimeBased.ExitStrategies;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using Skender.Stock.Indicators;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// 1-minute timeframe execution strategy.
    /// Implements IExecutionStrategy from Core.
    ///
    /// Handles precise entry timing and active position management.
    /// Uses polymorphic entry/exit strategies created via factory methods
    /// based on the setup's recommended strategy types.
    /// </summary>
    public class RegimeBasedExecutionStrategy : IExecutionStrategy
    {
        private QuoteHub<IQuote> _quoteHub;
        private readonly SetupResult _setup;

        public IEntryStrategy EntryStrategy { get; set; }
        public IExitStrategy ExitStrategy { get; set; }
        public decimal Quantity { get; set; }

        public RegimeBasedExecutionStrategy(QuoteHub<IQuote> quoteHub, SetupResult setup)
        {
            _setup = setup;
            Quantity = 0.1m; // Default, will be set by caller

            EntryStrategy = RegimeBasedEntryStrategyBase.Create(setup);
            ExitStrategy = RegimeBasedExitStrategyBase.Create(setup);

            SetQuotes(quoteHub);
        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            _quoteHub = quoteHub;
            EntryStrategy?.SetQuotes(quoteHub);
            ExitStrategy?.SetQuotes(quoteHub);
        }

        public decimal GetEntryPrice()
        {
            if (_setup == null) return 0;
            return (_setup.EntryZoneHigh + _setup.EntryZoneLow) / 2;
        }

        public StrategyStatus ProcessStrategy(ITrade trade)
        {
            var status = new StrategyStatus
            {
                StrategyAction = StrategyAction.NoAction,
                StrategyState = StrategyState.WaitingForEntry
            };

            if (_quoteHub?.Quotes == null || _quoteHub.Quotes.Count < 20)
                return status;

            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;

            // Not in trade - check for entry
            if (!trade.Open)
            {
                var entryDetails = EntryStrategy.GetNextEntry(0, Quantity, currentPrice);
                if (entryDetails.ShouldTrade)
                {
                    status.StrategyAction = StrategyAction.OpenTrade;
                    status.StrategyState = StrategyState.WaitingForEntry;
                }
                return status;
            }

            // In trade - check for exit
            status.StrategyState = StrategyState.WaitingForExit;
            var exitDetails = ExitStrategy.GetNextExit(trade.TotalOpenBaseQuantity, currentPrice, trade.ProfitPct);
            if (exitDetails.ShouldTrade)
            {
                status.StrategyAction = StrategyAction.CloseTrade;
                status.StrategyState = StrategyState.ExitSubmitted;
            }

            return status;
        }
    }
}