using CryptoTrading.App.Algorithm.RegimeBased.EntryStrategies;
using CryptoTrading.App.Algorithm.RegimeBased.ExitStrategies;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging;
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
        private ILogger _logger;

        public IEntryStrategy EntryStrategy { get; set; }
        public IExitStrategy ExitStrategy { get; set; }
        public decimal Quantity { get; set; }

        // Fee and minimum profit configuration
        // Round-trip fee rate: buy + sell fees combined (default 0.2% for Binance with BNB discount + margin)
        public decimal RoundTripFeeRate { get; set; } = 0.002m;
        // Minimum profit target after fees (default 0.5%)
        public decimal MinProfitRate { get; set; } = 0.005m;

        public RegimeBasedExecutionStrategy(QuoteHub<IQuote> quoteHub, SetupResult setup)
        {
            _setup = setup;
            Quantity = 0.1m; // Default, will be set by caller

            EntryStrategy = RegimeBasedEntryStrategyBase.Create(setup);
            var exitStrategy = RegimeBasedExitStrategyBase.Create(setup);
            exitStrategy.SetMinProfitThreshold(RoundTripFeeRate, MinProfitRate);
            ExitStrategy = exitStrategy;

            SetQuotes(quoteHub);
        }

        public void SetLogger(ILogger logger)
        {
            _logger = logger;
            (EntryStrategy as RegimeBasedEntryStrategyBase)?.SetLogger(logger);
            (ExitStrategy as RegimeBasedExitStrategyBase)?.SetLogger(logger);
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
            {
                _logger?.LogDebug($"[1M EXEC] Insufficient data ({_quoteHub?.Quotes?.Count ?? 0} quotes, need 20)");
                return status;
            }

            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;
            var ts = _quoteHub.Quotes.Last().Timestamp.ToString("yyyy-MM-dd HH:mm");

            // Not in trade - check for entry
            if (!trade.Open)
            {
                var entryDetails = EntryStrategy.GetNextEntry(0, Quantity, currentPrice);
                if (entryDetails.ShouldTrade)
                {
                    _logger?.LogInformation($"[1M {ts}] ENTRY SIGNAL: {_setup.RecommendedEntryStrategy} | Price: {currentPrice:F2} | Qty: {entryDetails.Quantity} | Type: {entryDetails.OrderType}");
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
                _logger?.LogInformation($"[1M {ts}] EXIT SIGNAL: {_setup.RecommendedExitStrategy} | Price: {currentPrice:F2} | PnL: {trade.ProfitPct:F2}% | Qty: {exitDetails.Quantity}");
                status.StrategyAction = StrategyAction.CloseTrade;
                status.StrategyState = StrategyState.ExitSubmitted;
                // Cache the exit decision so ExecuteExitStrategy doesn't call GetNextExit again
                status.ExitDetails = exitDetails;
            }

            return status;
        }
    }
}