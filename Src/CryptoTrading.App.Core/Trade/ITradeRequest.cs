using Binance;
using System;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITradeRequest
    {
        string BaseSymbol { get; }
        string QuoteSymbol { get; }
        decimal QuoteClosePrice { get; }
        public bool FixedAmount { get; set; }
        public double Amount { get; set; }
        DateTime? RequestDateTime { get; set; }
        IStopLimitTracker StopLimitTracker { get; set; }
        CandlestickInterval Interval { get; set; }
        decimal Volume { get; set; }
        decimal BaseQuantity { get; }
        decimal QuoteQuantity { get; }
        bool Validate(decimal freeAmount, decimal nonFreeAmount);
    }
}
