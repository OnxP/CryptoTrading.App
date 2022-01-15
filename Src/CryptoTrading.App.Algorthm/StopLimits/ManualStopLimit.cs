using Binance;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Algorithm.StopLimits
{
    class ManualStopLimit : StopLimitBase
    {
        private bool _triggerUpdate = false;
        public ManualStopLimit(decimal risk, decimal increment)
        {
            Risk = risk / 100m;
            Increment = increment / 100m;
        }
        public override void Configure(Order order)
        {
            base.Configure(order);
            _triggerUpdate = false;
        }
        public override void ManualChangeSL(decimal sl)
        {
            if (sl > StopLimitPrice)
            {
                StopLimitPrice = sl;
                _triggerUpdate = true;
            }
        }
        public override void MoveStopLimit()
        {
            //do nothing.
        }

        public override bool RequestUpdateOfStopLimit(decimal closePrice)
        {
            var update = _triggerUpdate;
            _triggerUpdate = false;
            return update;
        }
    }
}
