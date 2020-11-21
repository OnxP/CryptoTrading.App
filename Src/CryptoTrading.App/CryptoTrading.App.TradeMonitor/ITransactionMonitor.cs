using Binance;

namespace CryptoTrading.App.Monitor
{
    public interface ITransactionMonitor
    {
        bool Live { get; }
        string Symbol { get; }

        void Update(Order order);
        void Cancel(string order);
    }
}