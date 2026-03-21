using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Core.TradeRequest
{
    public class MarketTradeRequest : ITradeRequest
    {
        public string BaseSymbol => Pair.BaseAsset;
        public string QuoteSymbol => Pair.QuoteAsset;
        public decimal QuoteClosePrice { get; internal set; }
        public DateTime? RequestDateTime { get; set; }
        public IStopLimitTracker StopLimitTracker { get; set; }
        public CandleInterval Interval { get ; set; }
        public decimal QuoteQuantity { get; set; }
        public decimal BaseQuantity { get; set; }

        public bool Validate(decimal freeAmount, decimal nonFreeAmount)
        {
            //calculate quantity and stoploss limits
            var q = !FixedAmount ? (freeAmount +nonFreeAmount) * (decimal)Amount : (decimal)Amount;

            if (q > Volume * VolumeLimit)
            {
                q = Volume * VolumeLimit;
            }

            QuoteQuantity = AdjustForMinimum(Pair.TickSize, q, MidpointRounding.ToZero);
            BaseQuantity = AdjustForMinimum(Pair.StepSize, QuoteQuantity / QuoteClosePrice, MidpointRounding.ToZero); //btc

            var res = QuoteQuantity > Pair.MinNotional && CheckFee(BaseQuantity , QuoteClosePrice - StopLimitTracker.StopLimitPrice,QuoteQuantity);
            return res;
            //StopLimitTracker.Multiple = Math.Max(StopLimitTracker.Multiple, Pair.TickSize);
            //StopLimitTracker.SetLimits(QuoteClosePrice);
        }

        private bool CheckFee(decimal baseQuantity, decimal priceDiff, decimal quoteQuantity)
        {
            //return true;
            return quoteQuantity * 0.002m < (Math.Abs(priceDiff) * baseQuantity);
        }

        private decimal AdjustForMinimum(decimal increment, decimal calculateQuantity, MidpointRounding rounding)
        {
            //return calculateQuantity;
            int precision = (int)Math.Round(-Math.Log10((double)increment), 0);
            return Decimal.Round(calculateQuantity, precision, rounding);
        }

        public bool FixedAmount { get; set; }
        public double Amount { get; set; }
        public decimal Volume { get; set; }

        public decimal VolumeLimit { get; set; }
        public ExchangeSymbol Pair { get; set; }
    }
}
