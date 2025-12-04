using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database.RunIndicators.Indicators;
using Skender.Stock.Indicators;

namespace CryptoTrading.App.Algorithm
{
    public interface IStrategy
    {
        IStrategyResult Calculate(IMarketStructureResult marketStructure, out IExecutionStrategy executionStrategy);
        void SetQuotes(QuoteHub<IQuote> quoteHub);
    }

    public class Strategy : IStrategy
    {
        private QuoteHub<IQuote> _quoteHub;

        public IStrategyResult Calculate(IMarketStructureResult marketStructure, out IExecutionStrategy executionStrategy)
        {
            throw new System.NotImplementedException();


        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            _quoteHub = quoteHub;
        }
    }

}