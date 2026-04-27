using CryptoTrading.App.Core.Exchange;
using System;

namespace CryptoTrading.App.Core.Database
{
    public class CandleStickDb
    {
        public CandleStickDb()
        {

        }

        /// <summary>
        /// PR 6b: only neutral ctor remains. The bundled CandlestickInterval
        /// path was removed when CandleGapFiller and MissingCandleDetector
        /// switched to mapping bundled candles to ExchangeCandlestick
        /// immediately after the API call. ExchangeCandlestick does not
        /// carry the taker-buy volume breakdowns, so those columns are
        /// zeroed for new rows; existing rows already in the DB keep
        /// whatever value they were inserted with.
        /// </summary>
        public CandleStickDb(ExchangeCandlestick candlestick)
        {
            Symbol = candlestick.Symbol;
            Interval = candlestick.Interval;
            OpenTime = candlestick.OpenTime;
            Open = Convert.ToDouble(candlestick.Open);
            High = Convert.ToDouble(candlestick.High);
            Low = Convert.ToDouble(candlestick.Low);
            Close = Convert.ToDouble(candlestick.Close);
            Volume = candlestick.Volume;
            CloseTime = candlestick.CloseTime;
            QuoteAssetVolume = candlestick.QuoteVolume;
            NumberOfTrades = candlestick.NumberOfTrades;
            TakerBuyBaseAssetVolume = 0m;
            TakerBuyQuoteAssetVolume = 0m;
        }

        /// <summary>
        /// PR 5h: ConvertObject now returns the neutral
        /// <see cref="ExchangeCandlestick"/>. The bundled-SDK columns
        /// (TakerBuy*) remain on this row for back-compat with the EF schema
        /// but are not surfaced through the neutral type — neutral consumers
        /// only need OHLCV + timestamps + quote volume.
        /// </summary>
        public static ExchangeCandlestick ConvertObject(CandleStickDb stick)
        {
            return new ExchangeCandlestick
            {
                ExchangeId = null,
                Symbol = stick.Symbol,
                Interval = stick.Interval,
                OpenTime = stick.OpenTime,
                CloseTime = stick.CloseTime,
                Open = Convert.ToDecimal(stick.Open),
                High = Convert.ToDecimal(stick.High),
                Low = Convert.ToDecimal(stick.Low),
                Close = Convert.ToDecimal(stick.Close),
                Volume = stick.Volume,
                QuoteVolume = stick.QuoteAssetVolume,
                NumberOfTrades = stick.NumberOfTrades,
                IsClosed = true,
            };
        }

        public int ID { get; set; }
        public string Symbol { get; set; }

        /// <summary>
        /// Get the interval. PR 5f: retyped to neutral
        /// <see cref="CandleInterval"/>; EF persists the int ordinal, which
        /// matches the bundled CandlestickInterval values 0–14, so existing
        /// rows are read back unchanged.
        /// </summary>
        public CandleInterval Interval { get; set; }

        /// <summary>
        /// Get the open time.
        /// </summary>
        public DateTime OpenTime { get; set; }

        /// <summary>
        /// Get the open price in quote asset units.
        /// </summary>
        public double Open { get; set; }

        /// <summary>
        /// Get the high price in quote asset units.
        /// </summary>
        public double High { get; set; }

        /// <summary>
        /// Get the low price in quote asset units.
        /// </summary>
        public double Low { get; set; }

        /// <summary>
        /// Get the close price in quote asset units.
        /// </summary>
        public double Close { get; set; }

        /// <summary>
        /// Get the volume in base asset units.
        /// </summary>
        public decimal Volume { get; set; }

        /// <summary>
        /// Get the close time.
        /// </summary>
        public DateTime CloseTime { get; set; }

        /// <summary>
        /// Get the volume in quote asset units.
        /// </summary>
        public decimal QuoteAssetVolume { get; set; }

        /// <summary>
        /// Get the number of trades.
        /// </summary>
        public long NumberOfTrades { get; set; }

        /// <summary>
        /// Get the taker buy base asset volume.
        /// </summary>
        public decimal TakerBuyBaseAssetVolume { get; set; }

        /// <summary>
        /// Get the taker buy quote asset volume.
        /// </summary>
        public decimal TakerBuyQuoteAssetVolume { get; set; }

    }
}