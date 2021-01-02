using Binance;
using Binance.Client;
using System.Collections.Generic;

namespace CryptoTrading.App.Algorthm
{
    public interface IAlgorthm
    {
        public void ProcessHistoricMarketData(IEnumerable<Candlestick> candlesticks);

        public void ProcessLiveCandleStick(CandlestickEventArgs candlestickEventArgs);
    }
}
