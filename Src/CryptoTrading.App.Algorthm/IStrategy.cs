using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database.RunIndicators.Indicators;
using CryptoTrading.App.Core.Trade;
using Skender.Stock.Indicators;
using System.Collections.Generic;

namespace CryptoTrading.App.Algorithm
{
    public interface IStrategy
    {
        (IStrategyResult, IExecutionStrategy) Calculate(IMarketStructureResult marketStructure);
        void SetQuotes(QuoteHub<IQuote> quoteHub);
    }

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
                    executionStrategy = new MacdCrossoverStrategy(_quoteHub);
                    strategyResult=  new StrategyResult
                    {
                        PostTrade = true,
                        Amount = 0.1m,
                        Leverage = 3,
                        OrderSide = OrderSide.Buy
                    };
                    break;
                case MarketRegime.BearMarket:
                    executionStrategy = new MacdCrossoverStrategy(_quoteHub);
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

    internal class MacdCrossoverStrategy : IExecutionStrategy
    {
        public IReadOnlyList<MacdResult> Macd { get; set; }
        public MacdCrossoverStrategy(QuoteHub<IQuote> quoteHub)
        {
            Macd = quoteHub.Quotes.ToMacd();
        }

        public IEntryStrategy EntryStrategy { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
        public IExitStrategy ExitStrategy { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
        public decimal Quantity { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

        public decimal GetEntryPrice()
        {
            throw new System.NotImplementedException();
        }

        public StrategyStatus ProcessStrategy(ITrade trade)
        {
            throw new System.NotImplementedException();
        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            throw new System.NotImplementedException();
        }
    }
}