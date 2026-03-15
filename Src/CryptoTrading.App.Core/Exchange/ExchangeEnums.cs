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
}
