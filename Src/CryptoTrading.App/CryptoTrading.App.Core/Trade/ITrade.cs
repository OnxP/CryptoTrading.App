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
        ITransaction CurrentTransaction { get; }
        bool Open { get; set; }
        decimal CurrentPrice { get; set; }

        void CancelCurrentTransaction();
        void UpdateCurrentTransaction(Order order);
        ITransaction CreateStopLimitTransaction(decimal currentStopLimit);
        void CompleteTrade();
    }
}