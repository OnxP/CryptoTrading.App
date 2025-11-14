using System;
using System.Threading.Tasks;
using Binance.Client;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.MarketMonitorFactory
{
    public interface IMarketMonitor
    {
        Task<bool> CheckOrder(ITransaction order);
        void Subscribe(string symbol, string keyValue, Action<CandlestickEventArgs> processCandleStick);
        bool IsSubscribed(string symbol, string keyValue);
        void UnSubscribe(string symbol, string keyValue);
        object GetHistoricCandleSticks();
    }
}
