using System;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Algorithm.StopLimits
{
    class ManualTrailingStopLimit : StopLimitBase
    {
        public ManualTrailingStopLimit(decimal risk, decimal increment) : base()
        {
            Risk = risk/100;
            Increment = increment / 100m;
        }

        public ManualTrailingStopLimit(decimal risk, decimal increment, ISymbolCache symbolCache) : base(symbolCache)
        {
            Risk = risk/100;
            Increment = increment / 100m;
        }

        public override void Configure(ExchangeOrder order)
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
            var tickSize = _symbolCache.Get(Pair).TickSize;

            TargetPrice = AdjustForMinimum(tickSize,  Multiple + TargetPrice, MidpointRounding.ToPositiveInfinity);
            var sl = AdjustForMinimum(tickSize, StopLimitPrice + Multiple, MidpointRounding.ToPositiveInfinity);

            StopLimitPrice = CurrentPrice > sl ? sl : AdjustForMinimum(tickSize, CurrentPrice - Multiple, MidpointRounding.ToPositiveInfinity);
        }
        private decimal AdjustForMinimum(decimal increment, decimal calculateQuantity, MidpointRounding rounding)
        {
            //return calculateQuantity;
            int precision = (int)Math.Round(-Math.Log10((double)increment), 0);
            return Decimal.Round(calculateQuantity, precision, rounding);
        }
    }
}
