using Binance;
using System;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITradeRequest
    {
        string BaseSymbol { get; set; }
        string QuoteSymbol { get; set; }
        decimal Price { get; }
        public bool FixedAmount { get; set; }
        public double Amount { get; set; }
        DateTime? RequestDateTime { get; set; }
        IStopLimitTracker StopLimitTracker { get; set; }
        CandlestickInterval Interval { get; set; }
        decimal CalculateQuantity(decimal freeAmount, decimal nonFreeAmount);
    }
}
