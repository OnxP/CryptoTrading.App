using System.Collections.Generic;
using Binance;
using Binance.Client;

namespace CryptoTrading.App.Core
{
    public interface IAlgorithm
    {
        public void ProcessHistoricMarketData(IEnumerable<Candlestick> candlesticks);
        public void ProcessLiveCandleStick(CandlestickEventArgs candlestickEventArgs);
        void Configure(IConfig config);
    }
}
