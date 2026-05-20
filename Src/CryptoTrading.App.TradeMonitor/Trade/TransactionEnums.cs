namespace CryptoTrading.App.Monitor.Trade
{
    public enum TransactionType
    {
        StopLimitTransaction,
        Transaction,
        MarketTransaction,
        LimitTransaction
    }

    public enum TransactionStatus { Pending, Completed, Cancelled }
}
