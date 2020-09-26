namespace CryptoTrading.App.Broker
{
    public interface IMarket
    {
        object GetAccountBalances();
        void GetPendingTransactions();
    }
}