using Binance.Client;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core
{
    public interface IMarketMonitor
    {
        string Symbol { get; set; }
        bool CheckOrder(string clientOrderId);
        void StopStream();
        void Dispose();
        void StartStream();
        void Subscribe(Action<CandlestickEventArgs> processCandleStick);
    }
}
