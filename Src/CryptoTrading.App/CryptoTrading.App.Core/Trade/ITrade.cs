using Binance;
using System;

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
        decimal Profit { get; }
        decimal StartPrice { get; }
        DateTime StartDate { get; }
        DateTime CloseDate { get; }
        IStopLimitTracker StopLimitTracker { get; set; }

        void CancelCurrentTransaction();
        void UpdateCurrentTransaction(Order order);
        ITransaction CreateStopLimitTransaction(decimal currentStopLimit, DateTime? closeTime = null);
        void CompleteTrade();
    }
}