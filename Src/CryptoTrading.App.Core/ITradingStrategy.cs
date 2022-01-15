using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using System.Collections.Generic;
using Tulip;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public interface ITradingStrategy
    {
        //public Dictionary<string, (Indicator indicator, double[] options)> Indicators { get; }
        int OutputLength { get; }

        double Calculate(OrderedFixedLengthList<Candlestick> candleSticks, IStopLimitTracker StopLimitTrackers);
        void Log(string v);
    }
}
