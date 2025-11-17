using Binance;
using Binance.Client;
using CryptoTrading.App.Core.Trade;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.MarketMonitorFactory
{
    public interface IMarketMonitor
    {
        Task<bool> CheckOrder(ITransaction order);
        Task Subscribe(string symbol, string keyValue, Action<CandlestickEventArgs> processCandleStick);
        bool IsSubscribed(string symbol, string keyValue);
        void UnSubscribe(string symbol, string keyValue);
        Task<List<Candlestick>> GetHistoricCandleSticks(string symbol);
    }
}
