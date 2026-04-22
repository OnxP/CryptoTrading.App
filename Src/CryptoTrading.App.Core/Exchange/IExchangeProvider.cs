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

        /// <summary>
        /// The venue this provider is bound to. Consumers use this to pick
        /// sizing / PnL conventions (spot net position vs futures signed position).
        /// </summary>
        TradingVenue Venue { get; }

        // ---- Orders ----

        /// <summary>
        /// Place a market order (immediate execution at best price).
        /// </summary>
        /// <param name="positionSide">
        /// Futures-only. In dual-position mode, specifies which side of the
        /// book this order opens/closes. Ignored on spot/margin. Defaults to
        /// <see cref="PositionSide.Both"/> (one-way mode).
        /// </param>
        /// <param name="marginSideEffect">
        /// Margin-only. Controls auto-borrow / auto-repay. Ignored on
        /// spot/futures. Defaults to <see cref="MarginSideEffect.None"/>.
        /// </param>
        /// <param name="reduceOnly">
        /// Futures-only. When true, the order may only reduce an existing
        /// position (never flip or increase). Ignored on spot/margin.
        /// </param>
        Task<ExchangeOrder> PlaceMarketOrderAsync(
            string symbol,
            ExchangeOrderSide side,
            decimal quantity,
            PositionSide positionSide = PositionSide.Both,
            MarginSideEffect marginSideEffect = MarginSideEffect.None,
            bool reduceOnly = false);

        /// <summary>
        /// Place a limit order (execution at specified price or better).
        /// </summary>
        Task<ExchangeOrder> PlaceLimitOrderAsync(
            string symbol,
            ExchangeOrderSide side,
            decimal price,
            decimal quantity,
            PositionSide positionSide = PositionSide.Both,
            MarginSideEffect marginSideEffect = MarginSideEffect.None,
            bool reduceOnly = false);

        /// <summary>
        /// Place a stop-limit order.
        /// </summary>
        Task<ExchangeOrder> PlaceStopLimitOrderAsync(
            string symbol,
            ExchangeOrderSide side,
            decimal stopPrice,
            decimal limitPrice,
            decimal quantity,
            PositionSide positionSide = PositionSide.Both,
            MarginSideEffect marginSideEffect = MarginSideEffect.None,
            bool reduceOnly = false);

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
        /// Subscribe to real-time candlestick updates via WebSocket. The
        /// callback fires for BOTH intra-bar updates and the closed-bar
        /// event; consumers that only want closed bars must gate on
        /// <see cref="ExchangeCandlestick.IsClosed"/>.
        /// </summary>
        Task SubscribeCandlestickAsync(string symbol, CandleInterval interval, Action<ExchangeCandlestick> onCandle);

        /// <summary>
        /// Unsubscribe from all WebSocket streams.
        /// </summary>
        Task UnsubscribeAllAsync();

        // ---- Positions & Leverage (Futures/Margin) ----

        /// <summary>
        /// Get all open positions. On spot this returns an empty set; on
        /// margin/futures it maps to the exchange's per-symbol position rows.
        /// </summary>
        Task<IEnumerable<ExchangePosition>> GetPositionsAsync();

        /// <summary>
        /// Set the leverage for a symbol. Futures-only; no-op on spot and
        /// margin (margin leverage is a per-trade loan decision, not a
        /// symbol-wide setting).
        /// </summary>
        Task SetLeverageAsync(string symbol, int leverage);

        // ---- User Data (WebSocket) ----

        /// <summary>
        /// Subscribe to order-update / execution events for this account.
        /// Replaces REST polling of order state; each <see cref="ExchangeFill"/>
        /// represents a single execution (partial or full) with the resulting
        /// order status.
        /// </summary>
        Task SubscribeUserStreamAsync(Action<ExchangeFill> onFill);
    }
}
