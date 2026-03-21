using CryptoTrading.App.Core.Exchange;
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
                    executionStrategy = new BollingerBandBreakoutStrategy(_quoteHub,ExchangeOrderSide.Buy);
                    strategyResult=  new StrategyResult
                    {
                        PostTrade = true,
                        Amount = 0.1m,
                        Leverage = 3,
                        OrderSide = ExchangeOrderSide.Buy
                    };
                    break;
                case MarketRegime.BearMarket:
                    executionStrategy = new BollingerBandBreakoutStrategy(_quoteHub,ExchangeOrderSide.Sell);
                    strategyResult = new StrategyResult
                    {
                        PostTrade = true,
                        Amount = -0.1m,
                        Leverage = 3,
                        OrderSide = ExchangeOrderSide.Sell
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