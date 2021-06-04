using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using System.Collections.Generic;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public interface ITradingStrategy
    {
        //public Dictionary<string, (Indicator indicator, double[] options)> Indicators { get; }
        int OutputLength { get; }

        double Calculate(OrderedFixedLengthList<Candlestick> candleSticks);
        void Log(string v);
    }
}
