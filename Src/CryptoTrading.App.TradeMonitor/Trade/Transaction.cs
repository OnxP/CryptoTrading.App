using CryptoTrading.App.Broker.Position;
using CryptoTrading.App.Core.Exchange;
using System;

namespace CryptoTrading.App.Monitor.Trade
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
        public ExchangeOrder Order { get; private set; }

        public bool IsFilled => Order?.Status == ExchangeOrderStatus.Filled;
        public bool IsPartiallyFilled => Order?.Status == ExchangeOrderStatus.PartiallyFilled;

        internal void SetTransactionStatus(TransactionLegStatus status)
        {
            Quote.Status = status;
            Base.Status = status;
            Fee.Status = status;
        }

        public void UpdateOrder(ExchangeOrder order)
        {
            Order = order;
            switch (order.Status)
            {
                case ExchangeOrderStatus.New:
                    break;
                case ExchangeOrderStatus.PartiallyFilled:
                    //wait for fill fill.
                    break;
                case ExchangeOrderStatus.Filled:
                    UpdateTransactions(order);
                    SetTransactionStatus(TransactionLegStatus.Completed);
                    Status = TransactionStatus.Completed;
                    break;
                case ExchangeOrderStatus.Cancelled:
                case ExchangeOrderStatus.Rejected:
                case ExchangeOrderStatus.Expired:
                    SetTransactionStatus(TransactionLegStatus.Cancelled);
                    Status = TransactionStatus.Cancelled;
                    break;
            }
        }

        private void UpdateTransactions(ExchangeOrder order)
        {
            if(order.Side == ExchangeOrderSide.Buy)
            {
                Base.Quantity = order.FilledQuantity;
                Quote.Quantity = -order.QuoteQuantity;
            }
            if (order.Side == ExchangeOrderSide.Sell)
            {
                Base.Quantity = -order.FilledQuantity;
                Quote.Quantity = order.QuoteQuantity;
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
}
