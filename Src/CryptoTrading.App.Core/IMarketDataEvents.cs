using System;
using System.Collections.Generic;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core
{
    /// <summary>
    /// Market-data subscription surface. All types are neutral
    /// (<see cref="ExchangeCandlestick"/> / <see cref="ExchangeCandlestickEvent"/> /
    /// <see cref="CandleInterval"/>) so consumers don't depend on the bundled
    /// Binance SDK. Callers that still hold a bundled <c>CandlestickInterval</c>
    /// (e.g. via <see cref="IConfig.Interval"/>) translate at the seam via
    /// <c>BundledSdkBridge</c> until PR 5d retypes <see cref="IConfig"/> itself.
    /// </summary>
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
