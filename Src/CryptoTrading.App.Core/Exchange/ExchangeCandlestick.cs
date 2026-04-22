using System;

namespace CryptoTrading.App.Core.Exchange
{
    /// <summary>
    /// Exchange-agnostic candlestick/OHLCV data (replaces Binance.Candlestick).
    /// Represents a single candle from any exchange.
    /// </summary>
    public class ExchangeCandlestick
    {
        public string ExchangeId { get; set; }
        public string Symbol { get; set; }
        public CandleInterval Interval { get; set; }
        public DateTime OpenTime { get; set; }
        public DateTime CloseTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public decimal QuoteVolume { get; set; }
        public long NumberOfTrades { get; set; }

        /// <summary>
        /// True when this candle represents a closed bar. Websocket streams
        /// emit intra-bar updates (IsClosed=false) plus the final close event
        /// (IsClosed=true). Consumers that need bar-close semantics must gate
        /// on this flag. REST-sourced historical candles are always closed.
        /// </summary>
        public bool IsClosed { get; set; } = true;

        public ExchangeCandlestick() { }

        public ExchangeCandlestick(
            string exchangeId, string symbol, CandleInterval interval,
            DateTime openTime, DateTime closeTime,
            decimal open, decimal high, decimal low, decimal close,
            decimal volume)
        {
            ExchangeId = exchangeId;
            Symbol = symbol;
            Interval = interval;
            OpenTime = openTime;
            CloseTime = closeTime;
            Open = open;
            High = high;
            Low = low;
            Close = close;
            Volume = volume;
        }

        /// <summary>
        /// Whether this is a bullish candle (close >= open)
        /// </summary>
        public bool IsBullish => Close >= Open;

        /// <summary>
        /// The body size of the candle (absolute difference between open and close)
        /// </summary>
        public decimal BodySize => Math.Abs(Close - Open);

        /// <summary>
        /// The full range of the candle (high - low)
        /// </summary>
        public decimal Range => High - Low;
    }
}
