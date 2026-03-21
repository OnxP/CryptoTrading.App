namespace CryptoTrading.App.Core.Exchange
{
    public enum ExchangeOrderSide
    {
        Buy,
        Sell
    }

    public enum ExchangeOrderType
    {
        Market,
        Limit,
        StopLimit
    }

    public enum ExchangeOrderStatus
    {
        New,
        PartiallyFilled,
        Filled,
        Cancelled,
        Rejected,
        Expired
    }

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
