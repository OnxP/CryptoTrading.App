using Binance;
using CryptoTrading.App.Broker;
using CryptoTrading.App.Core.TradeRequest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CryptoTrading.App.Core.Trade
{
    public class Trade : ITrade
    {
        public Trade(IPosition buyPosition, IPosition sellPosition, IPosition feePosition, ITradeRequest request)
        {
            BuyPosition = buyPosition;
            SellPosition = sellPosition;
            FeePosition = feePosition;
            CreateNewTransaction(request);
        }

        public Transaction CurrentTransaction => Transactions.Last();
        public List<Transaction> Transactions { get; set; }
        public decimal Price => CurrentTransaction.Price;
        public string Symbol => CurrentTransaction.Pair;
        public OrderSide OrderType => Math.Sign(CurrentTransaction.Base.Quantity) > 0 ? OrderSide.Buy : OrderSide.Sell;
        public decimal Quantity => CurrentTransaction.Base.Quantity;
        public bool Open { get; set; }
        public IPosition BuyPosition { get; }
        public IPosition SellPosition { get; }
        public IPosition FeePosition { get; }

        public void CancelCurrentTransaction()
        {
            throw new NotImplementedException();
        }

        public Transaction CreateNewTransaction(ITradeRequest request)
        {
            var quoteQuantity = request.SellAmount == 0 ? SellPosition.FreeAmount * (decimal)request.SellPercentage : request.SellAmount;
            var quantity = quoteQuantity / request.Price;
            var transaction = CreateTransaction(BuyPosition.CreateTransaction(quantity), SellPosition.CreateTransaction(-quoteQuantity), FeePosition.CreateTransaction(quoteQuantity / 0.002m), request.Price, request.RequestDateTime);
            Transactions.Add(transaction);
            return transaction;
        }

        public Transaction CreateStopLimitTransaction(decimal currentStopLimit)
        {
            throw new NotImplementedException();
        }

        public void CreateStopLimitTransaction(object stopLimitValue)
        {
            throw new NotImplementedException();
        }

        public Transaction CreateTransaction(TransactionLeg baseLeg, TransactionLeg quoteLeg, TransactionLeg feeLeg, decimal price, DateTime? transactionDT)
        {
            var t = new Transaction();
            t.Base = baseLeg;
            t.Quote = quoteLeg;
            t.Fee = feeLeg;
            t.Price = price;
            t.TransactionDate = transactionDT ?? DateTime.Now;
            return t;
        }

        public void UpdateCurrentTransaction()
        {
            throw new NotImplementedException();
        }

        public void UpdateCurrentTransaction(Order order)
        {
            throw new NotImplementedException();
        }
    }
}
 