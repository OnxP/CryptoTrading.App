using System;
using System.Collections.Generic;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core
{
    public interface IMarketDataEvents
    {
        public void InitialDataLoadSubscribe(string symbol, CandleInterval interval,
            Action<IEnumerable<ExchangeCandlestick>> callback);

        public void InitialDataLoadUnSubscribe(string symbol, CandleInterval interval);

        public void InitialDataStreamSubscribe(string symbol, CandleInterval interval,
            Action<ExchangeCandlestickEvent> callback);

        public void InitialDataStreamUnSubscribe(string symbol, CandleInterval interval);
    }
}
