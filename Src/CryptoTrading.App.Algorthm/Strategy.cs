using Binance;
using CryptoTrading.App.Core.Strategy;
using Skender.Stock.Indicators;

namespace CryptoTrading.App.Algorithm
{
    public class Strategy : IStrategy
    {
        private QuoteHub<IQuote> _quoteHub;

        public (IStrategyResult, IExecutionStrategy) Calculate(IMarketStructureResult marketStructure)
        {
            //simple MacD cross over strategy
            var executionStrategy = null as IExecutionStrategy;
            var strategyResult = null as StrategyResult;

            switch (marketStructure.MarketRegime)
            {
                case MarketRegime.BullMarket:
                    executionStrategy = new BollingerBandBreakoutStrategy(_quoteHub,OrderSide.Buy);
                    strategyResult=  new StrategyResult
                    {
                        PostTrade = true,
                        Amount = 0.1m,
                        Leverage = 3,
                        OrderSide = OrderSide.Buy
                    };
                    break;
                case MarketRegime.BearMarket:
                    executionStrategy = new BollingerBandBreakoutStrategy(_quoteHub,OrderSide.Sell);
                    strategyResult = new StrategyResult
                    {
                        PostTrade = true,
                        Amount = -0.1m,
                        Leverage = 3,
                        OrderSide = OrderSide.Sell
                    };
                    break;
                default:
                    executionStrategy = null;
                    strategyResult = new StrategyResult
                    {
                        PostTrade = false
                    };
                    break;
            }
            return (strategyResult, executionStrategy);
        }
  
        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            _quoteHub = quoteHub;
        }
    }
}