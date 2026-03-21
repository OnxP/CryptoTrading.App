using CryptoTrading.App.Core.Exchange;
using Skender.Stock.Indicators;
using System.Collections.Generic;

namespace CryptoTrading.App.Core.Strategy
{
    public interface IExecutionStrategy
    {
        IEntryStrategy EntryStrategy { get; set; }
        IExitStrategy ExitStrategy { get; set; }
        decimal Quantity {get;set;}
        decimal GetEntryPrice();
        StrategyStatus ProcessStrategy(Trade.ITrade trade);
        void SetQuotes(QuoteHub<IQuote> quoteHub);
    }
}
