using Binance.Client;
using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Core
{
    public interface IMarketMonitor
    {
        string Symbol { get; set; }
        bool Started { get; }
        bool CheckOrder(ITransaction order);
        void StopStream();
        void Dispose();
        void StartStream();
        void Subscribe(Action<CandlestickEventArgs> processCandleStick);
    }
}
