using Binance;
using Binance.Client;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core
{
    public interface IMarketMonitor
    {
        string Symbol { get; set; }
        bool Started { get; }
        bool CheckOrder(Order order);
        void StopStream();
        void Dispose();
        void StartStream();
        void Subscribe(Action<CandlestickEventArgs> processCandleStick);
    }
}
