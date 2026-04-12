using CryptoTrading.App.Core.Strategy;
using Skender.Stock.Indicators;

namespace CryptoTrading.App.Algorithm.HtfRsiVolExpansion
{
    /// <summary>
    /// Entry strategy for HTF RSI + Vol Expansion.
    /// Enters immediately with a market order when the algorithm fires a setup.
    /// All entry condition validation happens in the algorithm - this just executes.
    /// </summary>
    public class HtfRsiVolExpansionEntryStrategy : IEntryStrategy
    {
        private QuoteHub<IQuote> _quoteHub;

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            _quoteHub = quoteHub;
        }

        public TradeDetails GetNextEntry(decimal currentPositionSize, decimal targetPositionSize, decimal close)
        {
            // Already at target size - no further entry needed
            if (currentPositionSize >= targetPositionSize || targetPositionSize <= 0)
                return new TradeDetails { ShouldTrade = false };

            var remaining = targetPositionSize - currentPositionSize;
            return new TradeDetails
            {
                ShouldTrade = true,
                EntryPrice = close,
                Quantity = remaining,
                Price = close,
                OrderType = "MARKET"
            };
        }
    }
}
