using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Monitor.StopLimitTracker
{
    class FixedProfitStopLimit : IStopLimitTracker
    {
        public decimal StopLimitPrice { get; set; }

        public decimal TargetPrice { get; set; }
        public decimal CurrentPrice { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public void Configure(Order order)
        {
            var price = order.Price;

            StopLimitPrice = price * 0.99m;
            TargetPrice = price * 1.02m;
        }

        public void Dispose()
        {
        }

        public void MoveStopLimit()
        {
            StopLimitPrice = TargetPrice;
        }
    }
}
