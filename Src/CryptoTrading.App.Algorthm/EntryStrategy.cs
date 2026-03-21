using CryptoTrading.App.Core.Strategy;
using Skender.Stock.Indicators;

namespace CryptoTrading.App.Algorithm
{
    //looking at using the sRSI to get the entry point.
    //there will be multiple entry stratiges
    //seperate ones for long and short.
    public class EntryStrategy : IEntryStrategy
    {
        public TradeDetails GetNextEntry(decimal currentPositionSize, decimal targetPositionSize, decimal close)
        {
            throw new System.NotImplementedException();
        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            throw new System.NotImplementedException();
        }
    }
}
