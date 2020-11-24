using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Monitor
{
    interface IStopLimitTracker
    {
        decimal StopLimitValue { get; set; }
        void Configure(Order order);
        void MoveStopLimit();
    }
}
