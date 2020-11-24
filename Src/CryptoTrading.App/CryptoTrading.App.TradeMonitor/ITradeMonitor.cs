using Binance;

namespace CryptoTrading.App.Monitor
{
    public interface ITradeMonitor
    {
        bool Live { get; }
        string Symbol { get; }

        void UpdateInitialTransaction(Order order);
        void CancelLimitOrder(string order);
        void StartStopLossMonitor(Order order);
    }
}