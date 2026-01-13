using Skender.Stock.Indicators;

namespace CryptoTrading.App.Core
{
    public interface IEntryStrategy
    {
        TradeDetails GetNextEntry(decimal currentPositionSize, decimal targetPositionSize, decimal close);
        void SetQuotes(QuoteHub<IQuote> quoteHub);
    }

    public class TradeDetails
    {
        public bool ShouldTrade { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public object OrderType { get; set; }
    }
}
