using Binance;

namespace CryptoTrading.App.Core.TradeRequest
{
    public interface IMarketRequest : IRequest
    {
        OrderSide? OrderType { get; }
        decimal Quantity { get; }
        decimal Price { get; }
    }
}
