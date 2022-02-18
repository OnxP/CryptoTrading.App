using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tulip;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class CompositeTradingStrategy : ITradingStrategy
    {
        public List<ITradingStrategy> tradingStrategies { get; }

        public CompositeTradingStrategy(IEnumerable<ITradingStrategy> strategies)
        {
            tradingStrategies = strategies.ToList();
        }
        public int OutputLength => tradingStrategies.Max(x => x.OutputLength);

        public double Calculate(CandleStickDictionary candleSticks,IStopLimitTracker stopLimitTrackers)
        {
            return tradingStrategies.Sum(x => x.Calculate(candleSticks, stopLimitTrackers));
        }

        public void Log(string v)
        {
            tradingStrategies.ForEach(x => x.Log(v));
        }
    }
}
