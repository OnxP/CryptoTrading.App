using Binance;
using CryptoTrading.App.Core.Position;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Core.Trade
{
    public class Trade : ITrade
    {
        public Trade(IPosition buyPosition, IPosition sellPosition, IPosition feePosition)
        {
            BuyPosition = buyPosition;
            SellPosition = sellPosition;
            FeePosition = feePosition;
            Transactions = new List<ITransaction>();
        }

        public ITradeRequest InitialRequest { get; set; }
        public ITransaction GetCurrentTransaction()
        {
            return Transactions.Last();
        }

        public List<ITransaction> Transactions { get; set; }
        public decimal Price => GetCurrentTransaction().Price;
        public string Pair => GetCurrentTransaction().Pair;
        public OrderSide OrderType => Math.Sign(GetCurrentTransaction().Base.Quantity) > 0 ? OrderSide.Buy : OrderSide.Sell;
        public decimal Quantity => GetCurrentTransaction().Base.Quantity;
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
                var current = GetCurrentTransaction().Quote;
                var diff = current.Quantity - Math.Abs(first.Quantity);

                return Math.Round(diff, 9);
            }
        }
        public decimal FeeBnb
        {
            get
            {
                var first = Transactions.First().Fee; //is negative
                var current = GetCurrentTransaction().Fee;
                var diff = Math.Abs(current.Quantity) + Math.Abs(first.Quantity);

                return Math.Round(diff, 9);
            }
        }

        public decimal Profit
        {
            get
            {
                var first = Transactions.First().Quote;//is negative
                var current = GetCurrentTransaction().Quote;

                var percentDiff = ((current.Quantity - Math.Abs(first.Quantity)) / Math.Abs(first.Quantity)) * 100;

                return Math.Round(percentDiff,2);
            }
        }

        public decimal StartPrice => Transactions.First().Price;

        public DateTime StartDate => Transactions.First().TransactionDate;

        public DateTime CloseDate => GetCurrentTransaction().TransactionDate;

        public string Comment => $"Stop Limit Hit: {Transactions.Count==2}";

        public void CancelCurrentTransaction()
        {
            GetCurrentTransaction().Cancel();
        }

        public ITransaction CompleteTrade()
        {
            GetCurrentTransaction().Cancel();
            var closeTransaction = CreateStopLimitTransaction(CurrentPrice);
            closeTransaction.Complete();
            return closeTransaction;
        }

        //public ITransaction CreateNewTransaction()
        //{
        //    var transaction = CreateTransaction<MarketTransaction>(BuyPosition.CreatePendingTransaction(InitialRequest.BaseQuantity),
        //        SellPosition.CreatePendingTransaction(-InitialRequest.QuoteQuantity),
        //        CalculateFee(FeePosition, InitialRequest.QuoteSymbol, InitialRequest.QuoteQuantity), InitialRequest.QuoteClosePrice, InitialRequest.RequestDateTime);
        //    Transactions.Add(transaction);
        //    return transaction;
        //}

        private TransactionLeg CalculateFee(IPosition FeePosition, string quoteSymbol, decimal quoteQuantity)
        {
            //for now assume fee is 0.075% of quote currency -> USDT
            var feeAmount = -Math.Abs(InitialRequest.Amount * 0.075m);
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
            if (GetCurrentTransaction() != null)
            {
                GetCurrentTransaction().UpdateOrder(order);
            }
        }

        public ITransaction CreateNewTransaction(decimal price, DateTime closeTime, decimal amount)
        {
            var transaction = CreateTransaction<MarketTransaction>(BuyPosition.CreatePendingTransaction(amount),
                SellPosition.CreatePendingTransaction(-amount * price),
                CalculateFee(FeePosition, BuyPosition.Symbol, amount), price, closeTime);
            Transactions.Add(transaction);
            return transaction;
        }
    }
}
public sealed class TradePlan
{
    // “How much exposure do we want?”
    public decimal TargetBaseQty { get; init; }      // e.g. +0.01 BTC for long, -0.01 BTC for short
    public decimal EntrySliceQty { get; init; }      // optional: how much each entry order adds
    public decimal ExitSliceQty { get; init; }      // optional: how much each exit order removes
    public decimal? EntryLimitPrice { get; init; }   // optional: if using limit placement
    public decimal? ExitLimitPrice { get; init; }   // optional
}
