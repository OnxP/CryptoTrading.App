using Binance;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITrade
    {
        decimal Price { get; }
        string Symbol { get; }
        OrderSide OrderType { get; }
        decimal Quantity { get; }
    }
}