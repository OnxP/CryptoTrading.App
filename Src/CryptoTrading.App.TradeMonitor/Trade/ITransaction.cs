using CryptoTrading.App.Broker.Position;
using CryptoTrading.App.Core.Exchange;
using System;

namespace CryptoTrading.App.Monitor.Trade
{
    public interface ITransaction
    {
        TransactionType Type { get; }
        string Pair { get; }
        decimal Price { get; }
        TransactionLeg Quote { get; }
        TransactionLeg Base { get; }
        TransactionLeg Fee { get; }
        ExchangeOrder Order { get; }
        TransactionStatus Status { get; set; }
        DateTime TransactionDate { get; set; }
        bool IsFilled { get; }
        bool IsPartiallyFilled { get; }
        void UpdateOrder(ExchangeOrder order);
        void Cancel();
        void Complete();
    }
}
