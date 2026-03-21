using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using Skender.Stock.Indicators;
using System.Collections.Generic;

namespace CryptoTrading.App.Algorithm
{
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