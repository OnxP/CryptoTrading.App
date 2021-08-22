using Binance;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Algorthm.StopLimits
{
    class FixedProfitStopLimit : IStopLimitTracker
    {
        private decimal _risk = 3.48m / 100m;
        private decimal _increment = 1.52m / 100m;
        public FixedProfitStopLimit(decimal risk, decimal increment)
        {
            _risk =1-( risk / 100m);
            _increment = 1+(increment / 100m);
        }
        public decimal StopLimitPrice { get; set; }
        public DateTime EndDateTime { get; set; }

        public decimal TargetPrice { get; set; }
        public decimal CurrentPrice { get; set; }

        public void Configure(Order order)
        {
            var price = order.Price;

            StopLimitPrice = price * _risk;
            TargetPrice = price * _increment;
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
