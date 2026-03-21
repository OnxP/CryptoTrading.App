using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core.TradeRequest
{
    public interface IMarketRequest : IRequest
    {
        ExchangeOrderSide? OrderType { get; }
        decimal Quantity { get; }
        decimal Price { get; }
    }
}
