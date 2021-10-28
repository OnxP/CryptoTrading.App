using Binance;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Algorthm.StopLimits
{
    class FixedTrailingStopLimit : StopLimitBase
    {
        private int i = 2;
        private decimal _boughtPrice;
        public FixedTrailingStopLimit(decimal risk, decimal increment)
        {
            Risk = 1 - (risk / 100m);
            //_fixed = 1 + (increment / 100m);
        }

        public override void Configure(Order order)
        {
            //set stopLimitValue to 10% of current price.
            _boughtPrice = order.Price;
            IsOpen = true;
            //i = 1;
        }

        public override void MoveStopLimit()
        {
            if (i == 1)
            {
                Increment = (CurrentPrice - _boughtPrice) / _boughtPrice;
                StopLimitPrice = _boughtPrice * 1.002m;
                TargetPrice += CurrentPrice * Increment;
            }
            else
            {
                TargetPrice += TargetPrice * Increment;
                StopLimitPrice += StopLimitPrice * Increment;
            }
            i++;
        }
    }
}
