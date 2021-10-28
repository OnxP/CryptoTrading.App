using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core
{
    public interface IStopLimitTracker
    {
        decimal StopLimitPrice { get; set; }
        decimal TargetPrice { get; set; }
        void Configure(Order order);
        void MoveStopLimit();
        void Dispose();
        decimal CurrentPrice { get; set; }
        public DateTime EndDateTime { get; set; }
        bool IsOpen { get; set; }
        decimal Increment { get; set; }
        decimal Risk { get; set; }

        bool RequestUpdateOfStopLimit(decimal closePrice);
        void ManualChangeSL(decimal sl);
    }
}
