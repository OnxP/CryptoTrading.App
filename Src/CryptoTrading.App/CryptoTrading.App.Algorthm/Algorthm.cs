using Binance;
using Binance.Client;
using CryptoTrading.App.Algorthm.TradingStrategies;
using System.Collections.Generic;
using Tulip;

namespace CryptoTrading.App.Algorthm
{
    public class Algorthm
    {
        //
        public List<ITradingStrategy> tradingStrategies { get; set; }
        public void ProcessHistoricMarketData(IEnumerable<Candlestick> candlesticks)
        {
            //foreach()
        }

        public void ProcessLiveCandleStick(CandlestickEventArgs candlestickEventArgs)
        {

        }
    }
}
