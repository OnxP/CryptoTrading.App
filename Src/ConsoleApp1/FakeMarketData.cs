using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using System;
using System.Collections.Generic;

namespace CryptoTrading.App.BackTesting
{
    /// <summary>
    /// Minimal IMarketDataEvents that captures the callbacks the algorithm
    /// registers so the driver can invoke them to replay candles.
    /// </summary>
    internal class FakeMarketData : IMarketDataEvents
    {
        public Action<IEnumerable<ExchangeCandlestick>> HistoricLoad15M;
        public Action<IEnumerable<ExchangeCandlestick>> HistoricLoad4H;
        public Action<ExchangeCandlestickEvent> LiveStream15M;
        public Action<ExchangeCandlestickEvent> LiveStream4H;

        public void InitialDataLoadSubscribe(string symbol, CandleInterval interval,
            Action<IEnumerable<ExchangeCandlestick>> callback)
        {
            if (interval == CandleInterval.Minute_15) HistoricLoad15M = callback;
            else if (interval == CandleInterval.Hour_4) HistoricLoad4H = callback;
        }

        public void InitialDataLoadUnSubscribe(string symbol, CandleInterval interval) { }

        public void InitialDataStreamSubscribe(string symbol, CandleInterval interval,
            Action<ExchangeCandlestickEvent> callback)
        {
            if (interval == CandleInterval.Minute_15) LiveStream15M = callback;
            else if (interval == CandleInterval.Hour_4) LiveStream4H = callback;
        }

        public void InitialDataStreamUnSubscribe(string symbol, CandleInterval interval) { }

        public void FireHistoric15M(IEnumerable<ExchangeCandlestick> candles) => HistoricLoad15M?.Invoke(candles);
        public void FireHistoric4H(IEnumerable<ExchangeCandlestick> candles) => HistoricLoad4H?.Invoke(candles);

        public void FireLive15M(ExchangeCandlestick c) =>
            LiveStream15M?.Invoke(new ExchangeCandlestickEvent(c, DateTime.UtcNow));

        public void FireLive4H(ExchangeCandlestick c) =>
            LiveStream4H?.Invoke(new ExchangeCandlestickEvent(c, DateTime.UtcNow));
    }
}
