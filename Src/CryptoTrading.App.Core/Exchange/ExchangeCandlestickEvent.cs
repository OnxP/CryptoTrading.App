using System;

namespace CryptoTrading.App.Core.Exchange
{
    public class ExchangeCandlestickEvent
    {
        public DateTime EventTime { get; set; }
        public ExchangeCandlestick Candlestick { get; set; }
        public long FirstTradeId { get; set; }
        public long LastTradeId { get; set; }
        public bool IsFinal { get; set; }
    }
}
