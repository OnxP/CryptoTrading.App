using CryptoTrading.App.Core.Database.RunIndicators.Indicators;
using Skender.Stock.Indicators;

namespace CryptoTrading.App.Core.Strategy
{
    public interface IStrategy
    {
        (IStrategyResult, IExecutionStrategy) Calculate(IMarketStructureResult marketStructure);
        void SetQuotes(QuoteHub<IQuote> quoteHub);
    }
}