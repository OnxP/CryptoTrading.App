using Binance;
using System;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITradeRequest
    {
        string Symbol { get; }
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
        IExecutionStrategy Strategy { get; set; }

        bool Validate(decimal freeAmount, decimal nonFreeAmount);
    }
}
