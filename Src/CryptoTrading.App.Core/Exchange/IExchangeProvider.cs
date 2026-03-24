using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.Exchange
{
    /// <summary>
    /// Unified exchange interface that abstracts away exchange-specific APIs.
    /// Each exchange (Binance, Bitfinex, etc.) implements this interface.
    /// Replaces the combination of IMarket, IAccountConfig, and exchange-specific market data.
    /// </summary>
    public interface IExchangeProvider
    {
        /// <summary>
        /// Unique identifier for this exchange (e.g. "Binance", "Bitfinex")
        /// </summary>
        string ExchangeId { get; }

        // ---- Account ----

        /// <summary>
        /// Get all account balances from the exchange
        /// </summary>
        Task<IEnumerable<ExchangeBalance>> GetBalancesAsync();

        /// <summary>
        /// Get all available trading pairs from the exchange
        /// </summary>
        Task<IEnumerable<ExchangeSymbol>> GetSymbolsAsync();

        /// <summary>
        /// Get the current fee schedule for this exchange
        /// </summary>
        Task<ExchangeFeeSchedule> GetFeeScheduleAsync();

        // ---- Orders ----

        /// <summary>
        /// Place a market order (immediate execution at best price)
        /// </summary>
        Task<ExchangeOrder> PlaceMarketOrderAsync(string symbol, ExchangeOrderSide side, decimal quantity);

        /// <summary>
        /// Place a limit order (execution at specified price or better)
        /// </summary>
        Task<ExchangeOrder> PlaceLimitOrderAsync(string symbol, ExchangeOrderSide side, decimal price, decimal quantity);

        /// <summary>
        /// Place a stop-limit order
        /// </summary>
        Task<ExchangeOrder> PlaceStopLimitOrderAsync(string symbol, ExchangeOrderSide side, decimal stopPrice, decimal limitPrice, decimal quantity);

        /// <summary>
        /// Get the current state of an order
        /// </summary>
        Task<ExchangeOrder> GetOrderAsync(string symbol, string orderId);

        /// <summary>
        /// Cancel an active order
        /// </summary>
        Task<ExchangeOrder> CancelOrderAsync(string symbol, string orderId);

        /// <summary>
        /// Get all open/active orders
        /// </summary>
        Task<IEnumerable<ExchangeOrder>> GetOpenOrdersAsync();

        // ---- Market Data (REST) ----

        /// <summary>
        /// Get historical candlestick data
        /// </summary>
        Task<IEnumerable<ExchangeCandlestick>> GetCandlesticksAsync(string symbol, CandleInterval interval, DateTime from, DateTime to);

        // ---- Market Data (WebSocket) ----

        /// <summary>
        /// Subscribe to real-time candlestick updates via WebSocket
        /// </summary>
        Task SubscribeCandlestickAsync(string symbol, CandleInterval interval, Action<ExchangeCandlestick> onCandle);

        /// <summary>
        /// Unsubscribe from all WebSocket streams
        /// </summary>
        Task UnsubscribeAllAsync();
    }
}
