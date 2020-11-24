using Binance;
using CryptoTrading.App.Core.TradeRequest;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITrade
    {
        decimal Price { get; }
        string Symbol { get; }
        OrderSide OrderType { get; }
        decimal Quantity { get; }
        Transaction CurrentTransaction { get; }
        bool Open { get; set; }

        void CancelCurrentTransaction();
        void UpdateCurrentTransaction(Order order);
        Transaction CreateStopLimitTransaction(decimal currentStopLimit);
        void CreateStopLimitTransaction(object stopLimitValue);
    }
}