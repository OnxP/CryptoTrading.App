using Binance;
using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Core.TradeRequest
{
    public class BuyTradeRequest : ITradeRequest
    {
        public string BaseSymbol { get; set; }
        public string QuoteSymbol { get; set; }
        public decimal Price { get; internal set; }
        public DateTime? RequestDateTime { get; set; }
        public IStopLimitTracker StopLimitTracker { get; set; }
        public CandlestickInterval Interval { get ; set; }
        public decimal CalculateQuantity(decimal freeAmount, decimal nonFreeAmount)
        {
            var q = !FixedAmount ? freeAmount * (decimal)Amount : (decimal)Amount;

            if (q > Volume * VolumeLimit)
            {
                q = Volume * VolumeLimit;
            }

            return q;
        }

        public bool FixedAmount { get; set; }
        public double Amount { get; set; }
        public decimal Volume { get; set; }
        public decimal VolumeLimit { get; set; }
    }
}
