using Binance;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Algorthm.StopLimits
{
    public class StopLimitBase : IStopLimitTracker
    {
        public decimal StopLimitPrice { get ; set ; }
        public decimal TargetPrice { get ; set ; }
        public decimal CurrentPrice { get ; set ; }
        public DateTime EndDateTime { get ; set ; }
        public bool IsOpen { get ; set ; }
        public decimal Increment { get ; set ; }
        public decimal Risk { get ; set ; }

        public virtual void Configure(Order order)
        {
            IsOpen = true;
        }

        public void Close()
        {
            IsOpen = false;
        }

        public virtual void ManualChangeSL(decimal sl)
        {
            
        }

        public virtual void MoveStopLimit()
        {
            StopLimitPrice = CurrentPrice;
        }

        public virtual bool RequestUpdateOfStopLimit(decimal closePrice)
        {
            return TargetPrice <= closePrice;
        }
    }
}
