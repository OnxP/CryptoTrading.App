using Binance;
using CryptoTrading.App.Core.Strategy;
using Skender.Stock.Indicators;

namespace CryptoTrading.App.Algorithm
{
    internal class SrsiEntryStrategy : IEntryStrategy
    {
        public SrsiEntryStrategy(QuoteHub<IQuote> quoteHub, OrderSide buy)
        {
        }

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