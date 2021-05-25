using Binance;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public class MovingAverangeTradingStrategy : TradingStrategy
    {
        public MovingAverangeTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        public MovingAverangeTradingStrategy(ILogger<TradingStrategy> logger, Indicator stratgy, double NoOfTrades):this(logger)
        {
            indicator = stratgy;
            noOfTrades = NoOfTrades;
        }

        protected override double StrategyWeight => 1.0d/ noOfTrades;
        private double noOfTrades = 1d;

        private Indicator indicator = Tulip.Indicators.kama;
        //public override int OutputLength => 1000;

        protected override Dictionary<string, (Indicator indicator, double[] options)> GenerateIndicators()
        {
            var dict = new Dictionary<string, (Indicator indicator, double[] options)>();
            dict.Add("25", (indicator, new double[] { 25 }));
            dict.Add("50", (indicator, new double[] { 50 }));
            dict.Add("100", (indicator, new double[] { 100 }));
            //dict.Add("200", (Tulip.Indicators.ema, new double[] { 200 }));
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice)
        {
            var MA25 = indicatorOutputs["25"][0].ToList();
            var MA50 = indicatorOutputs["50"][0].ToList();
            var MA100= indicatorOutputs["100"][0].ToList();
            //var MA200 = indicatorOutputs["200"][0].ToList();

            // log values

            var condition1 = MA50.Last() >= MA100.Last();
            var condition2 = MA100.Last() >= (double)closePrice.Close;
            
            if (condition1 && condition2)
            {
                LogResult(1);
                return StrategyWeight;
            }
            //check if long is trading sideways, need more entries to determin that!

            //check if long is in an uptrend.
            LogResult(0);
            return 0;
        }

        
    }
}
