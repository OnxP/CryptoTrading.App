using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class MacdBB : TradingStrategy
    {
        public MacdBB(ILogger<TradingStrategy> logger, ISymbolCache symbolCache) : base(logger, symbolCache)
        {
        }

        protected override double StrategyWeight => 1.0;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();
            double shortPeriod = 12;
            double longPeriod = 26;
            double signal = 9;
            //add indicators to dictionary
            dict.Add("BBands", new IndicatorSetUp(Tulip.Indicators.bbands, new double[] { 20, 2 }));
            dict.Add("MACD", new IndicatorSetUp(Tulip.Indicators.macd, new double[] { shortPeriod, longPeriod, signal }));
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var lower = indicatorOutputs["BBands"][0].ToList();
            var middle = indicatorOutputs["BBands"][1].ToList();
            var upper = indicatorOutputs["BBands"][2].ToList();
            var macd = indicatorOutputs["MACD"][0].ToList();
            var signal = indicatorOutputs["MACD"][1].ToList();
            var hist = indicatorOutputs["MACD"][2].ToList();

            lower.Reverse();
            middle.Reverse();
            macd.Reverse();
            signal.Reverse();


            var condition1 = (double)(closePrice.Close) < ((middle.First() + lower.First()) /2);
            var condition2 = macd.First() == Math.Min(macd.First(),Math.Min(macd.Skip(1).First(),macd.Skip(2).First()));
            var condition3 = (macd.Skip(2).First() - macd.Skip(1).First()) >= (macd.Skip(1).First() - macd.Skip(0).First());
            //var condition4 = kLine.Last()< 0.8;
            if (condition1 && condition2 && condition3)
            {
                SetStopLimit(indicatorOutputs, closePrice, StopLimitTrackers);
                return 1;
            }
            return 0;
        }

        
    }
}
