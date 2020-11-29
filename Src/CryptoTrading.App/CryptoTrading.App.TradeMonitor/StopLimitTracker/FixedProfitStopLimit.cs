using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Monitor.StopLimitTracker
{
    class FixedProfitStopLimit : IStopLimitTracker
    {
        public decimal StopLimitPrice { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public void Configure(Order order)
        {
            throw new NotImplementedException();
        }

        public void MoveStopLimit()
        {
            throw new NotImplementedException();
        }
    }
}
