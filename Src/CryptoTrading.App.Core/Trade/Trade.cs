using Binance;
using CryptoTrading.App.Core.Position;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Core.Trade
{
    public class Trade : ITrade
    {
        public Trade(IPosition buyPosition, IPosition sellPosition, IPosition feePosition, ITradeRequest request)
        {
            BuyPosition = buyPosition;
            SellPosition = sellPosition;
            FeePosition = feePosition;
            Transactions = new List<ITransaction>();
            InitialRequest = request;
            StopLimitTracker = request.StopLimitTracker;
            CreateNewTransaction();
        }

        public ITradeRequest InitialRequest { get; set; }
        public ITransaction CurrentTransaction => Transactions.Last();
        public List<ITransaction> Transactions { get; set; }
        public decimal Price => CurrentTransaction.Price;
        public string Pair => CurrentTransaction.Pair;
        public OrderSide OrderType => Math.Sign(CurrentTransaction.Base.Quantity) > 0 ? OrderSide.Buy : OrderSide.Sell;
        public decimal Quantity => CurrentTransaction.Base.Quantity;
        public bool Open { get; set; }
        public IPosition BuyPosition { get; }
        public IPosition SellPosition { get; }
        public IPosition FeePosition { get; }
        public decimal CurrentPrice { get ; set; }

        public decimal BtcProfit
        {
            get
            {
                var first = Transactions.First().Quote; //is negative
                var current = CurrentTransaction.Quote;
                var diff = current.Quantity - Math.Abs(first.Quantity);

                return Math.Round(diff, 9);
            }
        }
        public decimal FeeBnb
        {
            get
            {
                var first = Transactions.First().Fee; //is negative
                var current = CurrentTransaction.Fee;
                var diff = Math.Abs(current.Quantity) + Math.Abs(first.Quantity);

                return Math.Round(diff, 9);
            }
        }

        public decimal Profit
        {
            get
            {
                var first = Transactions.First().Quote;//is negative
                var current = CurrentTransaction.Quote;

                var percentDiff = ((current.Quantity - Math.Abs(first.Quantity)) / Math.Abs(first.Quantity)) * 100;

                return Math.Round(percentDiff,2);
            }
        }

        public decimal StartPrice => Transactions.First().Price;

        public DateTime StartDate => Transactions.First().TransactionDate;

        public DateTime CloseDate => CurrentTransaction.TransactionDate;

        public IStopLimitTracker StopLimitTracker { get; set; }
        public string Comment => $"Stop Limit Hit: {Transactions.Count==2}";

        public void CancelCurrentTransaction()
        {
            CurrentTransaction.Cancel();
        }

        public void CompleteTrade()
        {
            CurrentTransaction.Cancel();
            var closeTransaction = CreateStopLimitTransaction(CurrentPrice);
            closeTransaction.Complete();
        }

        public ITransaction CreateNewTransaction()
        {
            var transaction = CreateTransaction<MarketTransaction>(BuyPosition.CreatePendingTransaction(InitialRequest.BaseQuantity),
                SellPosition.CreatePendingTransaction(-InitialRequest.QuoteQuantity),
                CalculateFee(FeePosition, InitialRequest.QuoteSymbol, InitialRequest.QuoteQuantity), InitialRequest.QuoteClosePrice, InitialRequest.RequestDateTime);
            Transactions.Add(transaction);
            return transaction;
        }

        private TransactionLeg CalculateFee(IPosition FeePosition, string quoteSymbol, decimal quoteQuantity)
        {
            //for now assume fee is 0.075% of quote currency -> USDT
            var feeAmount = -Math.Abs(InitialRequest.QuoteQuantity * 0.075m);
            //TODO get current market price for fee calculation
            return FeePosition.CreatePendingTransaction(feeAmount);
        }



        public ITransaction CreateStopLimitTransaction(decimal currentStopLimit, DateTime? closeTime = null)
        {
            var symbol = Symbol.Cache.Get(Pair);
            var buyQuantity = -Transactions.First().Base.Quantity;
            var sellQuantity = Transactions.First().Base.Quantity * currentStopLimit;
            var feeQuantity = Transactions.First().Fee.Quantity;
            var transaction = CreateTransaction<StopLimitTransaction>(BuyPosition.CreatePendingTransaction(buyQuantity), 
                SellPosition.CreatePendingTransaction(sellQuantity), 
                FeePosition.CreatePendingTransaction(feeQuantity), currentStopLimit, closeTime);
            Transactions.Add(transaction);
            return transaction;
        }
        public ITransaction CreateTransaction<T>(TransactionLeg baseLeg, TransactionLeg quoteLeg, TransactionLeg feeLeg, decimal price, DateTime? transactionDT) where T : Transaction
        {
            T t = (T)Activator.CreateInstance(typeof(T));
            t.Base = baseLeg;
            t.Quote = quoteLeg;
            t.Fee = feeLeg;
            t.Price = price;
            t.TransactionDate = transactionDT ?? DateTime.Now;
            return t;
        }
        public void UpdateCurrentTransaction(Order order)
        {
            if (CurrentTransaction != null)
            {
                CurrentTransaction.UpdateOrder(order);
            }
        }
    }
}
 