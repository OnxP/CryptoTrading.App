using Binance;
using System;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITransaction
    {
        TransactionType Type { get; }
        string Pair { get; }
        decimal Price { get; }
        TransactionLeg Quote { get; }
        TransactionLeg Base { get; }
        TransactionLeg Fee { get; }
        Order Order { get; }
        TransactionStatus Status { get; set; }
        DateTime TransactionDate { get; set; }
        bool IsFilled { get; }
        bool IsPartiallyFilled { get; }
        void UpdateOrder(Order order);
        void Cancel();
        void Complete();
    }
}
