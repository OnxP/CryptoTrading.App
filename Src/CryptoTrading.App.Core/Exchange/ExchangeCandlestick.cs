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
        public decimal TakerBuyBaseAssetVolume { get; set; }
        public decimal TakerBuyQuoteAssetVolume { get; set; }

        public ExchangeCandlestick() { }

        public ExchangeCandlestick(
            string exchangeId, string symbol, CandleInterval interval,
            DateTime openTime, DateTime closeTime,
            decimal open, decimal high, decimal low, decimal close,
            decimal volume, decimal quoteVolume = 0, long numberOfTrades = 0,
            decimal takerBuyBaseAssetVolume = 0, decimal takerBuyQuoteAssetVolume = 0)
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
            QuoteVolume = quoteVolume;
            NumberOfTrades = numberOfTrades;
            TakerBuyBaseAssetVolume = takerBuyBaseAssetVolume;
            TakerBuyQuoteAssetVolume = takerBuyQuoteAssetVolume;
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
