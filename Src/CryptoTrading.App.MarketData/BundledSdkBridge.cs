using System;
using Binance;
using Binance.Client;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.MarketData
{
    /// <summary>
    /// Bridge helpers that translate between the neutral
    /// <see cref="ExchangeCandlestick"/> / <see cref="CandleInterval"/> types
    /// and the bundled-SDK Binance types (<see cref="Binance.Candlestick"/>,
    /// <see cref="Binance.CandlestickInterval"/>, <see cref="CandlestickEventArgs"/>).
    ///
    /// PR 5b rewires the three IMarketData/IMarketMonitor implementations to
    /// fetch market data via <see cref="IExchangeProvider"/> (Binance.Net)
    /// while keeping the public IMarketData*/IMarketMonitor interfaces on
    /// the bundled-SDK types so the 15+ algorithm/consumer call sites don't
    /// need to change in this PR. This class is the translation seam.
    ///
    /// A follow-up PR migrates the interfaces themselves to neutral types
    /// and deletes this bridge along with the bundled SDK.
    /// </summary>
    internal static class BundledSdkBridge
    {
        public static CandleInterval ToNeutralInterval(CandlestickInterval interval)
        {
            switch (interval)
            {
                case CandlestickInterval.Minute: return CandleInterval.Minute_1;
                case CandlestickInterval.Minutes_3: return CandleInterval.Minute_3;
                case CandlestickInterval.Minutes_5: return CandleInterval.Minute_5;
                case CandlestickInterval.Minutes_15: return CandleInterval.Minute_15;
                case CandlestickInterval.Minutes_30: return CandleInterval.Minute_30;
                case CandlestickInterval.Hour: return CandleInterval.Hour_1;
                case CandlestickInterval.Hours_2: return CandleInterval.Hour_2;
                case CandlestickInterval.Hours_4: return CandleInterval.Hour_4;
                case CandlestickInterval.Hours_6: return CandleInterval.Hour_6;
                case CandlestickInterval.Hours_8: return CandleInterval.Hour_8;
                case CandlestickInterval.Hours_12: return CandleInterval.Hour_12;
                case CandlestickInterval.Day: return CandleInterval.Day_1;
                case CandlestickInterval.Days_3: return CandleInterval.Day_3;
                case CandlestickInterval.Week: return CandleInterval.Week_1;
                case CandlestickInterval.Month: return CandleInterval.Month_1;
                default: return CandleInterval.Hour_1;
            }
        }

        public static CandlestickInterval ToBundledInterval(CandleInterval interval)
        {
            switch (interval)
            {
                case CandleInterval.Minute_1: return CandlestickInterval.Minute;
                case CandleInterval.Minute_3: return CandlestickInterval.Minutes_3;
                case CandleInterval.Minute_5: return CandlestickInterval.Minutes_5;
                case CandleInterval.Minute_15: return CandlestickInterval.Minutes_15;
                case CandleInterval.Minute_30: return CandlestickInterval.Minutes_30;
                case CandleInterval.Hour_1: return CandlestickInterval.Hour;
                case CandleInterval.Hour_2: return CandlestickInterval.Hours_2;
                case CandleInterval.Hour_4: return CandlestickInterval.Hours_4;
                case CandleInterval.Hour_6: return CandlestickInterval.Hours_6;
                case CandleInterval.Hour_8: return CandlestickInterval.Hours_8;
                case CandleInterval.Hour_12: return CandlestickInterval.Hours_12;
                case CandleInterval.Day_1: return CandlestickInterval.Day;
                case CandleInterval.Day_3: return CandlestickInterval.Days_3;
                case CandleInterval.Week_1: return CandlestickInterval.Week;
                case CandleInterval.Month_1: return CandlestickInterval.Month;
                default: return CandlestickInterval.Hour;
            }
        }

        /// <summary>
        /// Build a bundled-SDK <see cref="Candlestick"/> from the neutral type.
        /// The neutral candle does not carry taker-buy volume breakdowns or
        /// quote-asset volume in all paths, so we fill those with safe defaults
        /// (0 / <see cref="ExchangeCandlestick.QuoteVolume"/>). Consumers in
        /// this repo only read OHLCV + timestamps, so the defaults are fine.
        /// The bundled Candlestick ctor asserts non-negative volumes, which is
        /// why we clamp rather than pass through raw values that could be
        /// negative for edge-case monthly bars.
        /// </summary>
        public static Candlestick ToBundledCandlestick(ExchangeCandlestick c, CandlestickInterval interval)
        {
            var quoteVolume = c.QuoteVolume < 0 ? 0m : c.QuoteVolume;
            var numberOfTrades = c.NumberOfTrades < 0 ? 0 : c.NumberOfTrades;
            return new Candlestick(
                c.Symbol,
                interval,
                c.OpenTime,
                c.Open,
                c.High,
                c.Low,
                c.Close,
                c.Volume,
                c.CloseTime,
                quoteVolume,
                numberOfTrades,
                takerBuyBaseAssetVolume: 0m,
                takerBuyQuoteAssetVolume: 0m);
        }

        /// <summary>
        /// Wrap a neutral candle in the bundled <see cref="CandlestickEventArgs"/>
        /// shape that live-stream subscribers expect. The bundled event carries
        /// firstTradeId / lastTradeId from Binance's kline payload; we pass 0
        /// (which Binance.Candlestick treats as "no trade id") since Binance.Net
        /// doesn't surface these on the kline stream.
        /// </summary>
        public static CandlestickEventArgs ToBundledEventArgs(ExchangeCandlestick c, CandlestickInterval interval)
        {
            var candle = ToBundledCandlestick(c, interval);
            var eventTime = c.CloseTime == default ? DateTime.UtcNow : c.CloseTime;
            return new CandlestickEventArgs(eventTime, candle, firstTradeId: 0, lastTradeId: 0, isFinal: c.IsClosed);
        }
    }
}
