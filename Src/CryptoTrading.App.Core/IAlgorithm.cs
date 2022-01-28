using Binance;
using Binance.Client;
using CryptoTrading.App.Core.Database;
using System.Collections.Generic;
using CryptoTrading.App.Process;

namespace CryptoTrading.App.Algorithm
{
    public interface IAlgorithm
    {
        public void ProcessHistoricMarketData(IEnumerable<Candlestick> candlesticks);
        public void ProcessLiveCandleStick(CandlestickEventArgs candlestickEventArgs);
        void Configure(IConfig config);
    }
}
