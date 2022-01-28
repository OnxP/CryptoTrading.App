using Binance;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.MarketMonitorFactory
{
    public interface ITradeMonitor
    {
        bool Live { get; }
        string Symbol { get;}
        string KeyValue { get; set; }

        ITrade Trade { get; }
        void UpdateInitialTransaction(Order order);
        void CancelLimitOrder(string order);
        void UpdateStopLimitOrder(Order order);
        void AddTrade(ITrade trade);
    }
}