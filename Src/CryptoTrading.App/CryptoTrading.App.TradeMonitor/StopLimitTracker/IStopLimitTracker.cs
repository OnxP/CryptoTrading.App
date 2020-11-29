using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Monitor
{
    public interface IStopLimitTracker
    {
        decimal StopLimitPrice { get; }
        decimal TargetPrice { get; }
        void Configure(Order order);
        void MoveStopLimit();
    }
}
