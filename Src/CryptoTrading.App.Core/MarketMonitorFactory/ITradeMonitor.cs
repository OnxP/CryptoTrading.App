using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.MarketMonitorFactory
{
    public interface ITradeMonitor
    {
        bool Live { get; }
        string Symbol { get;}
        string KeyValue { get; set; }

        ITrade Trade { get; }
        void UpdateInitialTransaction(ExchangeOrder order);
        void CancelLimitOrder(string order);
        void UpdateStopLimitOrder(ExchangeOrder order);
        void AddTrade(ITrade trade);
    }
}
