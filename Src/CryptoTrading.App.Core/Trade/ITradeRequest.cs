using Binance;
using CryptoTrading.App.Core.Strategy;
using System;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITradeRequest
    {
        Symbol Symbol { get; }
        string BaseSymbol { get; }
        string QuoteSymbol { get; }
        public decimal Amount { get; }
        public int Leverage { get; }
        public OrderSide OrderSide { get; }
        DateTime? RequestDateTime { get; }
        IExecutionStrategy Strategy { get; set; }
        bool Validate(decimal freeAmount, decimal nonFreeAmount);
    }
}
