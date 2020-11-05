using Binance;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CryptoTrading.App.Core.Trade
{
    public class Trade : ITrade
    {
        public Transaction currentTransaction => Transactions.Last();
        public List<Transaction> Transactions { get; set; }
        public decimal Price => currentTransaction.Price;
        public string Symbol => currentTransaction.Pair;
        public OrderSide OrderType => Math.Sign(currentTransaction.Base.Quantity) > 0 ? OrderSide.Buy : OrderSide.Sell;
        public decimal Quantity => currentTransaction.Base.Quantity;
        public bool Open { get; set; }

        public void CreateTransaction(TransactionLeg baseLeg, TransactionLeg quoteLeg, TransactionLeg feeLeg, decimal price, DateTime? transactionDT)
        {
            var t = new Transaction();
            t.Base = baseLeg;
            t.Quote = quoteLeg;
            t.Fee = feeLeg;
            t.Price = price;
            t.TransactionDate = transactionDT ?? DateTime.Now;
        }

        public void CreateTransaction(TransactionLeg transactionLeg1, TransactionLeg transactionLeg2, TransactionLeg transactionLeg3, decimal price, object requestDateTime)
        {
            throw new NotImplementedException();
        }
    }
}
 