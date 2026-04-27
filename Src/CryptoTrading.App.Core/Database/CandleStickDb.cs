using Binance;
using CryptoTrading.App.Core.Exchange;
using System;

namespace CryptoTrading.App.Core.Database
{
    public class CandleStickDb
    {
        public CandleStickDb()
        {

        }
        public CandleStickDb(Candlestick candlestick)
        {
            Symbol = candlestick.Symbol;
            // PR 5f: column is now neutral CandleInterval; the bundled
            // CandlestickInterval ordinals are 1:1 with CandleInterval, so
            // the EF int column stays byte-identical.
            Interval = (CandleInterval)(int)candlestick.Interval;
            OpenTime = candlestick.OpenTime;
            Open =  Convert.ToDouble(candlestick.Open);
            High =  Convert.ToDouble(candlestick.High);
            Low =   Convert.ToDouble(candlestick.Low);
            Close = Convert.ToDouble(candlestick.Close);
            Volume = candlestick.Volume;
            CloseTime = candlestick.CloseTime;
            QuoteAssetVolume = candlestick.QuoteAssetVolume;
            NumberOfTrades = candlestick.NumberOfTrades;
            TakerBuyBaseAssetVolume = candlestick.TakerBuyBaseAssetVolume;
            TakerBuyQuoteAssetVolume = candlestick.TakerBuyQuoteAssetVolume;
        }

        /// <summary>
        /// PR 5h: neutral-typed ctor. Used by the backtest data-load and
        /// historic-stream paths that now hand neutral candles to the EF
        /// layer directly. ExchangeCandlestick does not carry the
        /// taker-buy volume breakdowns, so those columns are zeroed; existing
        /// rows already in the DB keep whatever value they were inserted with.
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