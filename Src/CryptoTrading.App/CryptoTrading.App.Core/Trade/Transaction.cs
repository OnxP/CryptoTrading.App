using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core.Trade
{
    public class Transaction
    {
        public string Pair => Quote.Symbol + Base.Symbol;
        public decimal Price { get; set; }
        public TransactionLeg Quote { get; set; }
        public TransactionLeg Base { get; set; }
        public TransactionLeg Fee { get; set; }
        public DateTime TransactionDate { get; set; }
        public Order Order { get; set; }
    }

    public class TransactionLeg
    {
        public string Symbol { get; set; }
        public decimal Quantity { get; set; }
    }
}
