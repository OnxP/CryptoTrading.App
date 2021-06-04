using System;
using System.Collections.Generic;
using Binance;
using Binance.Client;

namespace CryptoTrading.App.Core
{
    public interface IMarketDataEvents
    {
        public void InitialDataLoadSubscribe(string symbol, CandlestickInterval interval,
            Action<IEnumerable<Candlestick>> callback);

        public void InitialDataLoadUnSubscribe(string symbol, CandlestickInterval interval);

        public void InitialDataStreamSubscribe(string symbol, CandlestickInterval interval,
            Action<CandlestickEventArgs> callback);

        public void InitialDataStreamUnSubscribe(string symbol, CandlestickInterval interval,
            Action<CandlestickEventArgs> callback);
    }
}