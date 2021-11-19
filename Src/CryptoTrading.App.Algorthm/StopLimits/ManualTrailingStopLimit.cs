using Binance;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Algorthm.StopLimits
{
    class ManualTrailingStopLimit : StopLimitBase
    {
        public ManualTrailingStopLimit(decimal risk, decimal increment)
        {
            Risk = risk / 100m;
            Increment = increment / 100m;
        }

        public override void Configure(Order order)
        {
            //set stopLimitValue to 10% of current price.
            IsOpen = true;

        }

        public override void MoveStopLimit()
        {
            TargetPrice *= 1+ Increment;
            StopLimitPrice *= 1+ Increment;
        }
    }
}
