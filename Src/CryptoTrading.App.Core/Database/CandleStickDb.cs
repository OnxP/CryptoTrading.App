using Binance;
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
            Interval = candlestick.Interval;
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

        public static Candlestick ConvertObject(CandleStickDb stick)
        {
            return new Candlestick(
                
                stick.Symbol,
                stick.Interval,
                stick.OpenTime,
                Convert.ToDecimal(stick.Open),
                Convert.ToDecimal(stick.High),
                Convert.ToDecimal(stick.Low),
                Convert.ToDecimal(stick.Close),
                stick.Volume,
                stick.CloseTime,
                stick.QuoteAssetVolume,
                stick.NumberOfTrades,
                stick.TakerBuyBaseAssetVolume,
                stick.TakerBuyQuoteAssetVolume
            );
        }

        public int ID { get; set; }
        public string Symbol { get; set; }

        /// <summary>
        /// Get the interval.
        /// </summary>
        public CandlestickInterval Interval { get; set; }

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