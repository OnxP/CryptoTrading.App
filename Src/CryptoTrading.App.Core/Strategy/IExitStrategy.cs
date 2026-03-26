using Skender.Stock.Indicators;

namespace CryptoTrading.App.Core.Strategy
{
    public interface IExitStrategy
    {
        TradeDetails GetNextExit(decimal currentPositionSize, decimal close, decimal profit);
        void SetQuotes(QuoteHub<IQuote> quoteHub);

        /// <summary>
        /// Reset internal state for a new trade. Must be called between trades
        /// to prevent stale EntryPrice, BarsHeld, etc. from carrying over.
        /// </summary>
        void ResetForNewTrade();
    }
}
