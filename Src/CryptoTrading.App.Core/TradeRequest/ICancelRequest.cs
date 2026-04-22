namespace CryptoTrading.App.Core.TradeRequest
{
    public interface ICancelRequest : IRequest
    {
        // Exchange-neutral order identifier. Binance numeric order IDs and
        // Bitfinex UUIDs both serialize through this string safely.
        string ClientOrderId { get; set; }
    }

    public interface ICancelTradeRequest : IRequest
    {
        string ClientOrderId { get; set; }
    }
}
