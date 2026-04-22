namespace CryptoTrading.App.Core.Exchange
{
    /// <summary>
    /// Exchange-agnostic order side (replaces Binance.OrderSide)
    /// </summary>
    public enum ExchangeOrderSide
    {
        Buy,
        Sell
    }

    /// <summary>
    /// Exchange-agnostic order type (replaces Binance.OrderType)
    /// </summary>
    public enum ExchangeOrderType
    {
        Market,
        Limit,
        StopLimit
    }

    /// <summary>
    /// Exchange-agnostic order status (replaces Binance.OrderStatus)
    /// </summary>
    public enum ExchangeOrderStatus
    {
        New,
        PartiallyFilled,
        Filled,
        Cancelled,
        Rejected,
        Expired
    }

    /// <summary>
    /// Exchange-agnostic candle interval (replaces Binance.CandlestickInterval)
    /// </summary>
    public enum CandleInterval
    {
        Minute_1,
        Minute_3,
        Minute_5,
        Minute_15,
        Minute_30,
        Hour_1,
        Hour_2,
        Hour_4,
        Hour_6,
        Hour_8,
        Hour_12,
        Day_1,
        Day_3,
        Week_1,
        Month_1
    }

    /// <summary>
    /// Venue the runtime is trading against. Picks the broker / account
    /// wiring at startup; also gates which quantity semantics the algorithm
    /// emits (spot = 1x, margin = borrowed spot, futures = derivative position).
    /// </summary>
    public enum TradingVenue
    {
        /// <summary>Standard spot. Leverage is always 1. Default for back-compat.</summary>
        Spot,
        /// <summary>Binance cross- or isolated-margin spot with borrow/repay.</summary>
        Margin,
        /// <summary>Binance USD-M perpetual futures.</summary>
        Futures
    }

    /// <summary>
    /// Futures position side. Ignored on Spot/Margin. On Binance USD-M this
    /// maps to the dual-position-mode flag; in one-way mode, always Both.
    /// </summary>
    public enum PositionSide
    {
        Both,
        Long,
        Short
    }

    /// <summary>
    /// Margin-account side effects. On Binance these map to the
    /// sideEffectType param (NO_SIDE_EFFECT | MARGIN_BUY | AUTO_REPAY).
    /// Only relevant when <see cref="TradingVenue.Margin"/>.
    /// </summary>
    public enum MarginSideEffect
    {
        None,
        /// <summary>Auto-borrow the required quote (entering) or base (shorting).</summary>
        AutoBorrow,
        /// <summary>Auto-repay the outstanding loan from the trade proceeds (exiting).</summary>
        AutoRepay
    }
}
