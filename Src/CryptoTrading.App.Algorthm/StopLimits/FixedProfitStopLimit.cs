using Binance;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Algorthm.StopLimits
{
    class FixedProfitStopLimit : IStopLimitTracker
    {
        public decimal StopLimitPrice { get; set; }

        public decimal TargetPrice { get; set; }
        public decimal CurrentPrice { get; set; }

        public void Configure(Order order)
        {
            var price = order.Price;

            StopLimitPrice = price * 0.991m;
            TargetPrice = price * 1.0152m;
        }

        public void Dispose()
        {
        }

        public void MoveStopLimit()
        {
            StopLimitPrice = CurrentPrice;
        }
    }
}
