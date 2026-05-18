using Skender.Stock.Indicators;

namespace CryptoTrading.App.Core.Strategy
{
    public interface IExecutionStrategy
    {
        IEntryStrategy EntryStrategy { get; set; }
        IExitStrategy ExitStrategy { get; set; }
        decimal Quantity {get;set;}
        decimal GetEntryPrice();
        StrategyStatus ProcessStrategy(TradeState tradeState);
        void SetQuotes(QuoteHub<IQuote> quoteHub);
    }
}
