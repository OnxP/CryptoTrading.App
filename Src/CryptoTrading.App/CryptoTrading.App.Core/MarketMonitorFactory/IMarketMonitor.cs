using Binance.Client;
using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Core
{
    public interface IMarketMonitor
    {
        bool CheckOrder(ITransaction order);
        void Subscribe(string symbol, string keyValue, Action<CandlestickEventArgs> processCandleStick);
        bool IsSubscribed(string symbol, string keyValue);
        void UnSubscribe(string symbol, string keyValue);
    }
}
