using Binance;

namespace CryptoTrading.App.Monitor
{
    public interface ITradeMonitor
    {
        bool Live { get; }
        string Symbol { get; }

        void Update(Order order);
        void Cancel(string order);
    }
}