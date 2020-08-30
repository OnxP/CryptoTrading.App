using Binance;
using Binance.Client;
using CryptoTrading.App.Algorthm.TradingStrategies;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace CryptoTrading.App.Algorthm
{
    public class Algorthm
    {
        //
        public int NumberOfCandleSticksToKeep => tradingStrategies.Max(x=>x.OutputLength);
        public List<ITradingStrategy> tradingStrategies { get; }
        private FixedLengthList _closePrices;
        public Algorthm(List<ITradingStrategy> strategies)
        {
            tradingStrategies = strategies;
        }
        public void ProcessHistoricMarketData(IEnumerable<Candlestick> candlesticks)
        {
            
            foreach (var strategy in tradingStrategies)
            {

            }
        }

        public void ProcessLiveCandleStick(CandlestickEventArgs candlestickEventArgs)
        {

        }
    }
}
