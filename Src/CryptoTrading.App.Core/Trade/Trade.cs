using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Position;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Core.Trade
{
    public class Trade : ITrade
    {
        public Trade(IPosition basePosition, IPosition quotePosition, IPosition feePosition)
        {
            BasePosition = basePosition; //BTC
            QuotePosition = quotePosition; //USDT
            FeePosition = feePosition;
            OpenTransactions = new List<ITransaction>();
            CloseTransactions = new List<ITransaction>();
        }
        
        // Separate collections for open and close transactions
        public List<ITransaction> OpenTransactions { get; set; }
        public List<ITransaction> CloseTransactions { get; set; }

        // Combined view of all transactions (for backward compatibility if needed)
        public IEnumerable<ITransaction> AllTransactions => OpenTransactions.Concat(CloseTransactions);

        public ITransaction GetCurrentOpenTransaction() => OpenTransactions.LastOrDefault();
        public ITransaction GetCurrentCloseTransaction() => CloseTransactions.LastOrDefault();
        public List<ITransaction> PendingEntryTransactions => OpenTransactions.Where(t => t.Status == TransactionStatus.Pending).ToList();
        public List<ITransaction> PendingExitTransactions => CloseTransactions.Where(t => t.Status == TransactionStatus.Pending).ToList();

        // Returns the most recent transaction across both collections
        public ITransaction GetCurrentTransaction()
        {
            var lastOpen = GetCurrentOpenTransaction();
            var lastClose = GetCurrentCloseTransaction();

            if (lastOpen == null) return lastClose;
            if (lastClose == null) return lastOpen;

            return lastClose.TransactionDate >= lastOpen.TransactionDate ? lastClose : lastOpen;
        }

        public string Pair => BasePosition.Symbol + QuotePosition.Symbol;

        public ExchangeOrderSide OrderType
        {
            get
            {
                var currentTx = GetCurrentTransaction();
                return currentTx != null && Math.Sign(currentTx.Base.Quantity) > 0
                    ? ExchangeOrderSide.Buy
                    : ExchangeOrderSide.Sell;
            }
        }

        public bool Open { get; set; } = false;
        public IPosition BasePosition { get; }
        public IPosition QuotePosition { get; }
        public IPosition FeePosition { get; }
        public decimal CurrentPrice { get; set; }

        // Aggregated quantities across all open transactions
        public decimal TotalOpenBaseQuantity => OpenTransactions.Sum(t => t.Base.Quantity);
        public decimal TotalOpenQuoteQuantity => OpenTransactions.Sum(t => t.Quote.Quantity);

        // Aggregated quantities across all close transactions
        public decimal TotalCloseBaseQuantity => CloseTransactions.Sum(t => t.Base.Quantity);
        public decimal TotalCloseQuoteQuantity => CloseTransactions.Sum(t => t.Quote.Quantity);

        // Average entry price (weighted by quantity)
        public decimal AverageEntryPrice
        {
            get
            {
                if (!OpenTransactions.Any() || TotalOpenBaseQuantity == 0) return 0;
                return Math.Abs(TotalOpenQuoteQuantity / TotalOpenBaseQuantity);
            }
        }

        // Average exit price (weighted by quantity)
        public decimal AverageExitPrice
        {
            get
            {
                if (!CloseTransactions.Any() || TotalCloseBaseQuantity == 0) return 0;
                return Math.Abs(TotalCloseQuoteQuantity / TotalCloseBaseQuantity);
            }
        }

        public decimal Profit
        {
            get
            {
                if (!OpenTransactions.Any()) return 0;

                var totalOpenQuote = TotalOpenQuoteQuantity; // negative for buys
                var totalCloseQuote = TotalCloseQuoteQuantity; // positive for sells
                var diff = totalCloseQuote + totalOpenQuote; // net P&L

                return Math.Round(diff, 9);
            }
        }

        public decimal FeeBnb
        {
            get
            {
                var openFees = OpenTransactions.Sum(t => Math.Abs(t.Fee.Quantity));
                var closeFees = CloseTransactions.Sum(t => Math.Abs(t.Fee.Quantity));
                return Math.Round(openFees + closeFees, 9);
            }
        }

        public decimal ProfitPct
        {
            get
            {
                if (!OpenTransactions.Any()) return 0;

                var totalOpenQuote = Math.Abs(TotalOpenQuoteQuantity);
                if (totalOpenQuote == 0) return 0;

                var totalCloseQuote = TotalCloseQuoteQuantity;
                var percentDiff = ((totalCloseQuote - totalOpenQuote) / totalOpenQuote) * 100;

                return Math.Round(percentDiff, 2);
            }
        }

        public decimal OpenPrice
        {
            get
            {
                if (!OpenTransactions.Any()) return 0;
                var totalBaseQty = OpenTransactions.Sum(t => Math.Abs(t.Base.Quantity));
                if (totalBaseQty == 0) return 0;
                return OpenTransactions.Sum(t => t.Price * Math.Abs(t.Base.Quantity)) / totalBaseQty;
            }
        }
        public decimal ClosePrice
        {
            get
            {
                if (!CloseTransactions.Any()) return 0;
                var totalBaseQty = CloseTransactions.Sum(t => Math.Abs(t.Base.Quantity));
                if (totalBaseQty == 0) return 0;
                return CloseTransactions.Sum(t => t.Price * Math.Abs(t.Base.Quantity)) / totalBaseQty;
            }
        }
        public DateTime StartDate => OpenTransactions.FirstOrDefault()?.TransactionDate ?? DateTime.MinValue;
        public DateTime CloseDate => CloseTransactions.LastOrDefault()?.TransactionDate ?? DateTime.MinValue;

        public bool IsFullyClosed => Math.Abs(TotalOpenBaseQuantity + TotalCloseBaseQuantity) < 0.00000001m;
        public decimal RemainingQuantity => TotalOpenBaseQuantity + TotalCloseBaseQuantity;

        public string Comment => $"Open Txns: {OpenTransactions.Count}, Close Txns: {CloseTransactions.Count}, Fully Closed: {IsFullyClosed}";

        public void CancelCurrentTransaction()
        {
            GetCurrentTransaction()?.Cancel();
        }

        public void CancelCurrentOpenTransaction()
        {
            GetCurrentOpenTransaction()?.Cancel();
        }

        public void CancelCurrentCloseTransaction()
        {
            GetCurrentCloseTransaction()?.Cancel();
        }

        public ITransaction CompleteTrade()
        {
            GetCurrentCloseTransaction()?.Cancel();
            var closeTransaction = CreateCloseTransaction(CurrentPrice,DateTime.Now,TotalOpenBaseQuantity*-1);
            closeTransaction.Complete();
            return closeTransaction;
        }

        private TransactionLeg CalculateFee(IPosition feePosition, string quoteSymbol, decimal quoteQuantity)
        {
            var feeAmount = -Math.Abs(quoteQuantity * 0.075m);
            return feePosition.CreatePendingTransaction(feeAmount);
        }

        // Create an open (entry) transaction
        public ITransaction CreateOpenTransaction(decimal price, DateTime transactionTime , decimal QuoteAmount)//amount is in Quote currency
        {
            var qty = QuoteAmount;
            var transaction = CreateTransaction<MarketTransaction>(
                BasePosition.CreatePendingTransaction(qty / price),
                QuotePosition.CreatePendingTransaction(-qty),
                CalculateFee(FeePosition, QuotePosition.Symbol, qty),
                price,
                transactionTime);

            OpenTransactions.Add(transaction);
            Open = true;
            return transaction; 
        }

        // Create a close (exit) transaction - closes remaining position by default
        public ITransaction CreateCloseTransaction(decimal price, DateTime transactionTime, decimal BaseAmount) //Amount is in Base Currency
        {
            var qty = BaseAmount*-1; // Default: close entire position
            var transaction = CreateTransaction<StopLimitTransaction>(
                BasePosition.CreatePendingTransaction(qty),
                QuotePosition.CreatePendingTransaction(-qty * price),
                CalculateFee(FeePosition, QuotePosition.Symbol, Math.Abs(qty)),
                price,
                transactionTime);

            CloseTransactions.Add(transaction);

            if (IsFullyClosed)
            {
                Open = false;
            }

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

        public void UpdateCurrentTransaction(ExchangeOrder order)
        {
            GetCurrentTransaction()?.UpdateOrder(order);
        }

        public void UpdateCurrentOpenTransaction(ExchangeOrder order)
        {
            GetCurrentOpenTransaction()?.UpdateOrder(order);
        }

        public void UpdateCurrentCloseTransaction(ExchangeOrder order)
        {
            GetCurrentCloseTransaction()?.UpdateOrder(order);
        }
    }

    public sealed record TradePlan(
        decimal TargetBaseQty,
        decimal EntrySliceQty,
        decimal ExitSliceQty,
        decimal? EntryLimitPrice = null,
        decimal? ExitLimitPrice = null
    );
}