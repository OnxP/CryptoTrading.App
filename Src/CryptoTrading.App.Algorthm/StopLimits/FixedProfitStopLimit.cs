using Binance;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Algorithm.StopLimits
{
    class FixedProfitStopLimit : StopLimitBase
    {
        private decimal _risk = 3.48m / 100m;
        private decimal _increment = 1.52m / 100m;
        public FixedProfitStopLimit(decimal risk, decimal increment)
        {
            _risk =1-( risk / 100m);
            _increment = 1+(increment / 100m);
        }

        public override void Configure(Order order)
        {
            var price = order.Price;
            IsOpen = true;
            //StopLimitPrice = price * _risk;
            //TargetPrice = price * _increment;
        }
    }
}
