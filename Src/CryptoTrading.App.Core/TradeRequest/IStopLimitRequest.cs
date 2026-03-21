using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core.TradeRequest
{
    public interface IStopLimitRequest : IRequest
    {
        ExchangeOrderSide? OrderType { get; }
        decimal StopPrice { get; }
        decimal Quantity { get; }
    }
}
