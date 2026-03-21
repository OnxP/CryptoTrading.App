using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Strategy;
using System;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITradeRequest
    {
        ExchangeSymbol Symbol { get; }
        string BaseSymbol { get; }
        string QuoteSymbol { get; }
        decimal QuoteClosePrice { get; }
        public bool FixedAmount { get; set; }
        public decimal Amount { get; set; }
        public int Leverage { get; }
        public ExchangeOrderSide OrderSide { get; }
        DateTime? RequestDateTime { get; set; }
        IStopLimitTracker StopLimitTracker { get; set; }
        CandleInterval Interval { get; set; }
        IExecutionStrategy Strategy { get; set; }
        decimal Volume { get; set; }
        decimal BaseQuantity { get; }
        decimal QuoteQuantity { get; }
        bool Validate(decimal freeAmount, decimal nonFreeAmount);
    }
}
