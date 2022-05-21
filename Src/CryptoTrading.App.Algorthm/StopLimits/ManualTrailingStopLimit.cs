using System;
using System.Threading;
using Binance;

namespace CryptoTrading.App.Algorithm.StopLimits
{
    class ManualTrailingStopLimit : StopLimitBase
    {
        public ManualTrailingStopLimit(decimal risk, decimal increment)
        {
            Risk = risk/100;
            Increment = increment / 100m;
        }

        public override void Configure(Order order)
        {
            //set stopLimitValue to 10% of current price.
            IsOpen = true;

        }

        public override void SetLimits(decimal quoteClosePrice)
        {
            //var symbol = Symbol.Cache.Get(Symbol.Cache.Get(Pair));
            //TargetPrice = AdjustForMinimum(symbol.Price, quoteClosePrice + (Multiple * Risk));
            //StopLimitPrice = AdjustForMinimum(symbol.Price, quoteClosePrice - Multiple);
        }

        public override void MoveStopLimit()
        {
            //TargetPrice += Multiple * Increment;
            //StopLimitPrice += Multiple * Increment;
            //TargetPrice *= (1+ Increment);
            //StopLimitPrice *= (1+ Increment);
            var symbol = Symbol.Cache.Get(Symbol.Cache.Get(Pair));

            TargetPrice = AdjustForMinimum(symbol.Price,  Multiple + TargetPrice, MidpointRounding.ToPositiveInfinity);
            var sl = AdjustForMinimum(symbol.Price, StopLimitPrice + Multiple, MidpointRounding.ToZero);

            if (CurrentPrice > sl)
                StopLimitPrice = sl;
        }
        private decimal AdjustForMinimum(InclusiveRange symbolQuantity, decimal calculateQuantity,MidpointRounding rounding)
        {
            //return calculateQuantity;
            int precision = (int)Math.Round(-Math.Log10((double)symbolQuantity.Increment), 0);
            return Decimal.Round(calculateQuantity, precision, rounding);
        }
    }
}
