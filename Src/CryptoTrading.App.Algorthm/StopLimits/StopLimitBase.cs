using Binance;
using CryptoTrading.App.Core;
using System;

namespace CryptoTrading.App.Algorithm.StopLimits
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
        public decimal Multiple { get; set; }
        public string Pair { get; set; }

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

        public virtual void SetLimits(decimal quoteClosePrice)
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
