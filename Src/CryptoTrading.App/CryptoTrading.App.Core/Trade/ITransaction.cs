using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITransaction
    {
        TransactionType Type { get; }
        public string Pair { get; }
        public decimal Price { get; }
        public TransactionLeg Quote { get; }
        public TransactionLeg Base { get; }
        public TransactionLeg Fee { get; }
        public Order Order { get; }
        void UpdateOrder(Order order);
        void Cancel();
    }
}
