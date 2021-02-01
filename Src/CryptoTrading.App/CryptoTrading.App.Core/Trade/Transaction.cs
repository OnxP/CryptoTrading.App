using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core.Trade
{
    public class Transaction : ITransaction
    {
        public virtual TransactionType Type { get; set; } = TransactionType.Transaction;
        public string Pair => Quote.Symbol + Base.Symbol;
        public decimal Price { get; set; }
        public TransactionLeg Quote { get; set; }
        public TransactionLeg Base { get; set; }
        public TransactionLeg Fee { get; set; }
        public DateTime TransactionDate { get; set; }
        public Order Order { get; private set; }

        internal void SetTransactionStatus(TransactionStatus status)
        {
            Quote.Status = status;
            Base.Status = status;
            Fee.Status = status;
        }

        public void UpdateOrder(Order order)
        {
            Order = order;
            switch (order.Status)
            {
                case OrderStatus.New:
                    break;
                case OrderStatus.PartiallyFilled:
                    throw new NotImplementedException();
                case OrderStatus.Filled:
                    UpdateTransactions(order);
                    SetTransactionStatus(TransactionStatus.Completed);
                    break;
                case OrderStatus.Canceled:
                case OrderStatus.PendingCancel:
                case OrderStatus.Rejected:
                case OrderStatus.Expired:
                    SetTransactionStatus(TransactionStatus.Cancelled);
                    break;
            }
        }

        private void UpdateTransactions(Order order)
        {
            if(order.Side == OrderSide.Buy)
            {
                Quote.Quantity = order.ExecutedQuantity;
            }
            if (order.Side == OrderSide.Sell)
            {
                Quote.Quantity = -order.ExecutedQuantity;
            }

        }

        public void Cancel()
        {
            SetTransactionStatus(TransactionStatus.Cancelled);
        }
    }

    public class TransactionLeg
    {
        public string Symbol { get; set; }
        public decimal Quantity { get; set; }
        public TransactionStatus Status { get; set; } = TransactionStatus.Pending;
    }

    public enum TransactionStatus {Pending, Completed, Cancelled };
}
