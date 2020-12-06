using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core
{
    public interface IMarketMonitor
    {
        bool CheckOrder(string clientOrderId);
        void StopStream();
        void Dispose();
        void StartStream();
    }
}
