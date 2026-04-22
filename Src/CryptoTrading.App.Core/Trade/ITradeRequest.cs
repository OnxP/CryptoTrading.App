using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Strategy;
using System;

namespace CryptoTrading.App.Core.Trade
{
    /// <summary>
    /// A request to open/adjust a position on a trading pair. Everything
    /// on this contract is exchange-neutral so it can be routed to Binance
    /// (bundled SDK or Binance.Net), Bitfinex, or a backtest stub without
    /// callers having to know which venue is attached.
    /// </summary>
    public interface ITradeRequest
    {
        /// <summary>
        /// Trading pair string, e.g. "BTCUSDT".
        /// </summary>
        string Symbol { get; }
        string BaseSymbol { get; }
        string QuoteSymbol { get; }
        decimal Amount { get; }
        int Leverage { get; }
        ExchangeOrderSide OrderSide { get; }
        DateTime? RequestDateTime { get; }
        IExecutionStrategy Strategy { get; set; }
        bool Validate(decimal freeAmount, decimal nonFreeAmount);
    }
}
