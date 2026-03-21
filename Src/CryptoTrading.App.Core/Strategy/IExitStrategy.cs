using Skender.Stock.Indicators;

namespace CryptoTrading.App.Core.Strategy
{
    public interface IExitStrategy
    {
        TradeDetails GetNextExit(decimal currentPositionSize, decimal close, decimal profit);
        void SetQuotes(QuoteHub<IQuote> quoteHub);
    }
}
