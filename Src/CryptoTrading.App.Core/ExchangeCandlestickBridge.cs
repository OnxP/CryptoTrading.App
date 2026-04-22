using Binance;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core
{
    /// <summary>
    /// Core-side bridge that translates neutral <see cref="ExchangeCandlestick"/>
    /// back to bundled <see cref="Binance.Candlestick"/> for legacy strategy
    /// signatures (<see cref="Binance.Candlestick"/> closePrice). PR 5c migrates
    /// <see cref="CandleStickDictionary"/> storage + the three MarketData
    /// interfaces to the neutral type, but keeps the 30+ trading-strategy
    /// override signatures bundled to avoid a huge surface-area churn. PR 5d
    /// retypes the strategy layer to neutral and deletes this bridge.
    /// </summary>
    public static class ExchangeCandlestickBridge
    {
        public static Candlestick ToBundled(ExchangeCandlestick c, CandlestickInterval interval)
        {
            if (c == null) return null;
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

        public static Candlestick ToBundled(ExchangeCandlestick c)
        {
            if (c == null) return null;
            return ToBundled(c, (CandlestickInterval)(int)c.Interval);
        }

        public static ExchangeCandlestick ToNeutral(Candlestick c)
        {
            if (c == null) return null;
            return new ExchangeCandlestick
            {
                ExchangeId = null,
                Symbol = c.Symbol,
                Interval = (CandleInterval)(int)c.Interval,
                OpenTime = c.OpenTime,
                CloseTime = c.CloseTime,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume,
                QuoteVolume = c.QuoteAssetVolume,
                NumberOfTrades = c.NumberOfTrades,
                IsClosed = true,
            };
        }
    }
}
