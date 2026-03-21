using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class AroonStoch : TradingStrategy
    {
        public AroonStoch(ILogger<TradingStrategy> logger, ISymbolCache symbolCache) : base(logger, symbolCache)
        {
        }

        protected override double StrategyWeight => 1.0;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            //add indicators to dictionary
            double shortPeriod = 12;
            double longPeriod = 26;
            double signal = 9;
            var wma100 = new IndicatorSetUp(Tulip.Indicators.wma, new double[] { 100 });
            var wma50 = new IndicatorSetUp(Tulip.Indicators.wma, new double[] { 50 });
            var srsi = new IndicatorSetUp(Tulip.Indicators.stoch, new double[] { 14, 1, 3 });
            var aroon = new IndicatorSetUp(Tulip.Indicators.aroon, new double[] { 14 });


            dict.Add("LongEma", wma100);
            dict.Add("MediumEma", wma50);
            dict.Add("SRSI", srsi);
            dict.Add("AROON", aroon);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var longEma = indicatorOutputs["LongEma"][0].ToList();
            var mediumEma = indicatorOutputs["MediumEma"][0].ToList();
            var k = indicatorOutputs["SRSI"][0].ToList();
            var d = indicatorOutputs["SRSI"][1].ToList();
            var down = indicatorOutputs["AROON"][0].ToList();
            var up = indicatorOutputs["AROON"][1].ToList();




            var condition1 = mediumEma.Last() > longEma.Last();
            var condition4 = k.Last() >= d.Last();
            //var condition5 = k.Last() <= 80;
            var condition6 = up.Last() >= 70 && down.Last()<=30;
            //var condition4 = kLine.Last()< 0.8;
            if (condition1 && condition4 && condition6)
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
