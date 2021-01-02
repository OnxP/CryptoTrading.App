using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public class CompositeTradingStrategy : ITradingStrategy
    {
        public List<ITradingStrategy> tradingStrategies { get; }

        public CompositeTradingStrategy(IEnumerable<ITradingStrategy> strategies)
        {
            tradingStrategies = strategies.ToList();
        }
        public int OutputLength => tradingStrategies.Max(x => x.OutputLength);

        public double Calculate(OrderedFixedLengthList closePrices)
        {
            return tradingStrategies.Sum(x => x.Calculate(closePrices));
        }
    }
}
