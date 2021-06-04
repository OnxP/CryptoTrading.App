using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.MarketMonitorFactory
{
    public interface IMarketMonitorFactory
    {
        ITradeMonitor CreateMonitor(ITrade trade);
    }
}