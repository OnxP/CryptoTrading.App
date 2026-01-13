using Binance;
using System;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITrade
    {
        decimal Price { get; }
        string Pair { get; }
        OrderSide OrderType { get; }
        decimal Quantity { get; }
        ITransaction? CurrentTransaction { get; }
        
        bool Open { get; set; }
        decimal CurrentPrice { get; set; }
        decimal Profit { get; }
        decimal BtcProfit { get; }
        decimal StartPrice { get; }
        DateTime StartDate { get; }
        DateTime CloseDate { get; }
        string Comment { get; }
        decimal FeeBnb { get; }

        void CancelCurrentTransaction();
        void UpdateCurrentTransaction(Order order);
        ITransaction CreateStopLimitTransaction(decimal currentStopLimit, DateTime? closeTime = null);
        ITransaction CompleteTrade();
        ITransaction CreateNewTransaction(decimal price, DateTime closeTime, decimal amount);
    }
}