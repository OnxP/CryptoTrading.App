using Binance;
using System;

namespace CryptoTrading.App.Core.Trade
{
    public class Transaction : ITransaction
    {
        public TransactionStatus Status { get; set; } = TransactionStatus.Pending;
        public virtual TransactionType Type { get; set; } = TransactionType.Transaction;
        public string Pair => Base.Symbol + Quote.Symbol;
        public decimal Price { get; set; }
        public TransactionLeg Quote { get; set; }
        public TransactionLeg Base { get; set; }
        public TransactionLeg Fee { get; set; }
        public DateTime TransactionDate { get; set; }
        public Order Order { get; private set; }

        public bool IsFilled => Order?.Status == OrderStatus.Filled;
        public bool IsPartiallyFilled => Order?.Status == OrderStatus.PartiallyFilled;

        internal void SetTransactionStatus(TransactionLegStatus status)
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
                    //wait for fill fill.
                    break;
                case OrderStatus.Filled:
                    UpdateTransactions(order);
                    SetTransactionStatus(TransactionLegStatus.Completed);
                    Status = TransactionStatus.Completed;
                    break;
                case OrderStatus.Canceled:
                case OrderStatus.PendingCancel:
                case OrderStatus.Rejected:
                case OrderStatus.Expired:
                    SetTransactionStatus(TransactionLegStatus.Cancelled);
                    Status = TransactionStatus.Cancelled;
                    break;
            }
        }

        private void UpdateTransactions(Order order)
        {
            if(order.Side == OrderSide.Buy)
            {
                Base.Quantity = order.ExecutedQuantity;
                Quote.Quantity = -order.CummulativeQuoteAssetQuantity;
            }
            if (order.Side == OrderSide.Sell)
            {
                Base.Quantity = -order.ExecutedQuantity;
                Quote.Quantity = order.CummulativeQuoteAssetQuantity;
            }

        }

        public void Cancel()
        {
            SetTransactionStatus(TransactionLegStatus.Cancelled);
            Status = TransactionStatus.Cancelled;
        }

        public void Complete()
        {
            SetTransactionStatus(TransactionLegStatus.Completed);
            Status = TransactionStatus.Completed;
        }
    }

    public class TransactionLeg
    {
        public string Symbol { get; set; }
        public decimal Quantity { get; set; }
        public TransactionLegStatus Status { get; set; } = TransactionLegStatus.Pending;
    }

    public enum TransactionLegStatus {Pending, Completed, Cancelled };
    public enum TransactionStatus {Pending, Completed, Cancelled };
}
