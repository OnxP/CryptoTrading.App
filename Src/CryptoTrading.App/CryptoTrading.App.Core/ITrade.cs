using Binance;

namespace CryptoTrading.App.Core
{
    public interface ITrade
    {
        decimal Price { get; set; }
        string Symbol { get; set; }
        OrderSide OrderType { get; set; }
        decimal Quantity { get; set; }
    }
}