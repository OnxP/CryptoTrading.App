using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using Skender.Stock.Indicators;

namespace CryptoTrading.App.Algorithm
{
    public class ExecutionStrategy : IExecutionStrategy
    {
        public IEntryStrategy EntryStrategy { get; set; }
        public IExitStrategy ExitStrategy { get; set; }
        public decimal Quantity { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

        public StrategyStatus ProcessStrategy(ITrade trade)
        {
            //checks the trade to see the current status then runs the entry or exit stratgy accordingly
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
