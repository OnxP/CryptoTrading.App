using CryptoTrading.App.Core.Strategy;
using Skender.Stock.Indicators;

namespace CryptoTrading.App.Algorithm
{
    public class ExecutionStrategy : IExecutionStrategy
    {
        public IEntryStrategy EntryStrategy { get; set; }
        public IExitStrategy ExitStrategy { get; set; }
        public decimal Quantity { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

        public StrategyStatus ProcessStrategy(TradeState tradeState)
        {
            throw new System.NotImplementedException();
        }

        public decimal GetEntryPrice()
        {
            throw new System.NotImplementedException();
        }

        

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            EntryStrategy.SetQuotes(quoteHub);
            ExitStrategy.SetQuotes(quoteHub);
        }

        
    }
}
