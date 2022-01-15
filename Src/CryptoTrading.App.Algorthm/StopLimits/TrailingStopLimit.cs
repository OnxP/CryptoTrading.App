using Binance;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Algorithm.StopLimits
{
    class TrailingStopLimit : StopLimitBase
    {
        private int i = 1;

        public TrailingStopLimit(decimal risk, decimal increment)
        {
            Risk = risk / 100m;
            Increment = increment / 100m;
        }

        public override void Configure(Order order)
        {
            //set stopLimitValue to 10% of current price.
            CurrentPrice = order.Price;
            StopLimitPrice = CurrentPrice * (1 - Risk);
            TargetPrice = CurrentPrice * (1 + Increment);
            i = 1;
            //when the price is small, where a single sitoshi becomes more that 1% the risk and increments need to be adjusted.
            while (Math.Round(CurrentPrice, 9) == Math.Round(TargetPrice, 9))
            {
                Increment *= 2;
                Risk *= 2;

                StopLimitPrice = CurrentPrice * (1 - Risk);
                TargetPrice = CurrentPrice * (1 + Increment);
            }
        }

        public override void MoveStopLimit()
        {
            TargetPrice *= 1+ Increment;
            StopLimitPrice *= 1+ Increment;
            if (i == 1) StopLimitPrice = CurrentPrice * (1 - Increment);
            i++;

        }
    }
}
