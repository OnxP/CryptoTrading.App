using Binance;
using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Core.TradeRequest
{
    public class SellTradeRequest : ITradeRequest
    {
        public string BaseSymbol { get; set; }
        public string QuoteSymbol { get; set; }
        public decimal Price { get; set; }
        public bool FixedAmount { get; set; }
        public double Amount { get; set; }
        public DateTime? RequestDateTime { get; set; }
        public IStopLimitTracker StopLimitTracker { get; set; }
        public CandlestickInterval Interval { get; set; }

    }
}
