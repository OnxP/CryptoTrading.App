using CryptoTrading.App.Core.Exchange;
using System;

namespace CryptoTrading.App.Core.Database
{
    public class CandleStickDb
    {
        public CandleStickDb()
        {

        }
        public CandleStickDb(ExchangeCandlestick candlestick)
        {
            Symbol = candlestick.Symbol;
            Interval = candlestick.Interval;
            OpenTime = candlestick.OpenTime;
            Open =  Convert.ToDouble(candlestick.Open);
            High =  Convert.ToDouble(candlestick.High);
            Low =   Convert.ToDouble(candlestick.Low);
            Close = Convert.ToDouble(candlestick.Close);
            Volume = candlestick.Volume;
            CloseTime = candlestick.CloseTime;
            QuoteAssetVolume = candlestick.QuoteVolume;
            NumberOfTrades = candlestick.NumberOfTrades;
            TakerBuyBaseAssetVolume = candlestick.TakerBuyBaseAssetVolume;
            TakerBuyQuoteAssetVolume = candlestick.TakerBuyQuoteAssetVolume;
        }

        public static ExchangeCandlestick ConvertObject(CandleStickDb stick)
        {
            return new ExchangeCandlestick
            {
                Symbol = stick.Symbol,
                Interval = stick.Interval,
                OpenTime = stick.OpenTime,
                Open = Convert.ToDecimal(stick.Open),
                High = Convert.ToDecimal(stick.High),
                Low = Convert.ToDecimal(stick.Low),
                Close = Convert.ToDecimal(stick.Close),
                Volume = stick.Volume,
                CloseTime = stick.CloseTime,
                QuoteVolume = stick.QuoteAssetVolume,
                NumberOfTrades = stick.NumberOfTrades,
                TakerBuyBaseAssetVolume = stick.TakerBuyBaseAssetVolume,
                TakerBuyQuoteAssetVolume = stick.TakerBuyQuoteAssetVolume
            };
        }

        public int ID { get; set; }
        public string Symbol { get; set; }

        /// <summary>
        /// Get the interval.
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
