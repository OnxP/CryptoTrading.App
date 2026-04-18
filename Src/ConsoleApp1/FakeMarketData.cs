using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
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
        public Action<IEnumerable<Candlestick>> HistoricLoad15M;
        public Action<IEnumerable<Candlestick>> HistoricLoad4H;
        public Action<CandlestickEventArgs> LiveStream15M;
        public Action<CandlestickEventArgs> LiveStream4H;

        public void InitialDataLoadSubscribe(string symbol, CandlestickInterval interval,
            Action<IEnumerable<Candlestick>> callback)
        {
            if (interval == CandlestickInterval.Minutes_15) HistoricLoad15M = callback;
            else if (interval == CandlestickInterval.Hours_4) HistoricLoad4H = callback;
        }

        public void InitialDataLoadUnSubscribe(string symbol, CandlestickInterval interval) { }

        public void InitialDataStreamSubscribe(string symbol, CandlestickInterval interval,
            Action<CandlestickEventArgs> callback)
        {
            if (interval == CandlestickInterval.Minutes_15) LiveStream15M = callback;
            else if (interval == CandlestickInterval.Hours_4) LiveStream4H = callback;
        }

        public void InitialDataStreamUnSubscribe(string symbol, CandlestickInterval interval) { }

        public void FireHistoric15M(IEnumerable<Candlestick> candles) => HistoricLoad15M?.Invoke(candles);
        public void FireHistoric4H(IEnumerable<Candlestick> candles) => HistoricLoad4H?.Invoke(candles);

        public void FireLive15M(Candlestick c) =>
            LiveStream15M?.Invoke(new CandlestickEventArgs(DateTime.UtcNow, c, 0, 0, true));

        public void FireLive4H(Candlestick c) =>
            LiveStream4H?.Invoke(new CandlestickEventArgs(DateTime.UtcNow, c, 0, 0, true));
    }
}
