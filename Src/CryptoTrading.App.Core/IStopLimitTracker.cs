using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core
{
    public interface IStopLimitTracker
    {
        decimal StopLimitPrice { get; }
        decimal TargetPrice { get; }
        void Configure(Order order);
        void MoveStopLimit();
        void Dispose();
        decimal CurrentPrice { get; set; }
        public DateTime EndDateTime { get; set; }

    }
}
