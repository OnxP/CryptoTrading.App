using Binance;

namespace CryptoTrading.App.TradeMonitor
{
    public interface ITradeMonitor
    {
        bool Live { get; }
        string Symbol { get; }

        void Update(Order order);
        void Cancel(string order);
    }
}