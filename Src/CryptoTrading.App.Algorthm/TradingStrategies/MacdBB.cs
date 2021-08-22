using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public class MacdBB : TradingStrategy
    {
        public MacdBB(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        protected override double StrategyWeight => 1.0;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, (Indicator indicator, double[] options)> GenerateIndicators()
        {
            var dict = new Dictionary<string, (Indicator indicator, double[] options)>();
            double shortPeriod = 12;
            double longPeriod = 26;
            double signal = 9;
            //add indicators to dictionary
            dict.Add("BBands", (Tulip.Indicators.bbands, new double[] { 20, 2 }));
            dict.Add("MACD", (Tulip.Indicators.macd, new double[] { shortPeriod, longPeriod, signal }));
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
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
                LogResult(1);
                return 1;
            }
            //check if long is trading sideways, need more entries to determin that!

            //check if long is in an uptrend.
            LogResult(0);
            return 0;
        }

        
    }
}
