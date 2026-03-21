using System.Collections.Generic;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core
{
    public interface IAlgorithm
    {
        public void ProcessHistoricMarketData(IEnumerable<ExchangeCandlestick> candlesticks);
        public void ProcessLiveCandleStick(ExchangeCandlestickEvent candlestickEventArgs);
        void Configure(IConfig config);
    }
}
