using System;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.MarketMonitorFactory
{
    public interface IMarketMonitor
    {
        Task<bool> CheckOrder(ITransaction order);
        void Subscribe(string symbol, string keyValue, Action<ExchangeCandlestickEvent> processCandleStick);
        bool IsSubscribed(string symbol, string keyValue);
        void UnSubscribe(string symbol, string keyValue);
    }
}
