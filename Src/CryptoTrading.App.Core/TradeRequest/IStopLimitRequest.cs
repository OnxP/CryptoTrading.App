using Binance;

namespace CryptoTrading.App.Core.TradeRequest
{
    public interface IStopLimitRequest : IRequest
    {
        OrderSide? OrderType { get; }
        decimal StopPrice { get; set; }
        decimal Quantity { get; }
    }
}
