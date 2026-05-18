using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Strategy;
using Skender.Stock.Indicators;
using System.Collections.Generic;

namespace CryptoTrading.App.Algorithm
{
    internal class BollingerBandBreakoutStrategy : IExecutionStrategy
    {
        public IReadOnlyList<BollingerBandsResult> Bbands { get; set; }
        public BollingerBandBreakoutStrategy(QuoteHub<IQuote> quoteHub, ExchangeOrderSide buy)
        {
            Bbands = quoteHub.Quotes.ToBollingerBands();
            EntryStrategy = new SrsiEntryStrategy(quoteHub, buy);
        }

        public IEntryStrategy EntryStrategy { get; set; }
        public IExitStrategy ExitStrategy { get; set; }
        public decimal Quantity { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

        public decimal GetEntryPrice()
        {
            throw new System.NotImplementedException();
        }

        public StrategyStatus ProcessStrategy(TradeState tradeState)
        {
            throw new System.NotImplementedException();
        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            throw new System.NotImplementedException();
        }
    }
}