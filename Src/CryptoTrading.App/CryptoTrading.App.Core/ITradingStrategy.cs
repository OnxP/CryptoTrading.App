using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Text;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public interface ITradingStrategy
    {
        public Dictionary<string, (Indicator indicator, double[] options)> Indicators { get; }
        int OutputLength { get; }

        double Calculate(OrderedFixedLengthList closePrices);
    }
}
