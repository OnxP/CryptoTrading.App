using CryptoTrading.App.Core.Exchange;
using System;
using System.Collections.Generic;

namespace CryptoTrading.App.Monitor.Trade
{
    public interface ITrade
    {
        string Pair { get; }

        ITransaction GetCurrentTransaction();
        decimal TotalOpenBaseQuantity { get; }
        decimal TotalCloseBaseQuantity { get; }
        decimal RemainingQuantity { get; }
        bool Open { get; set; }
        decimal CurrentPrice { get; set; }
        decimal ProfitPct { get; }
        decimal Profit { get; }
        decimal OpenPrice { get; }
        decimal ClosePrice { get; }
        DateTime StartDate { get; }
        DateTime CloseDate { get; }
        string Comment { get; }
        decimal FeeBnb { get; }

        void CancelCurrentTransaction();
        void UpdateCurrentTransaction(ExchangeOrder order);
        ITransaction CompleteTrade();
        ITransaction CreateOpenTransaction(decimal price, DateTime closeTime, decimal amount);
        ITransaction CreateCloseTransaction(decimal price, DateTime closeTime, decimal amount);
        List<ITransaction> PendingEntryTransactions { get; }
        List<ITransaction> PendingExitTransactions { get; }
    }
}
