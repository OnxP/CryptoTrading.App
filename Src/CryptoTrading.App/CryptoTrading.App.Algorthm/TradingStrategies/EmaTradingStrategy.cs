using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public class EmaTradingStrategy : TradingStrategy
    {
        public EmaTradingStrategy(ILogger logger) : base(logger)
        {
        }

        protected override double StrategyWeight => 1.0;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, (Indicator indicator, double[] options)> GenerateIndicators()
        {
            var dict = new Dictionary<string, (Indicator indicator, double[] options)>();

            //add indicators to dictionary
            //for simple ema strat, we need slow fast and long, 13 21 and 200
            var fastEma = (Tulip.Indicators.ema, new double[] { 13 });
            var slowEma = (Tulip.Indicators.ema, new double[] { 21 });
            var longEma = (Tulip.Indicators.ema, new double[] { 100 });
            dict.Add("FastEma", fastEma);
            dict.Add("SlowEma", slowEma);
            dict.Add("LongEma", longEma);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, double closePrice)
        {
            var fastEma = indicatorOutputs["FastEma"][0].ToList();
            var slowEma = indicatorOutputs["SlowEma"][0].ToList();
            var longEma = indicatorOutputs["LongEma"][0].ToList();


            Logger.LogInformation($"Fast Ema: {fastEma.Last()}");
            Logger.LogInformation($"Slow Ema: {slowEma.Last()}");
            Logger.LogInformation($"Long Ema: {longEma.Last()}");
            Logger.LogInformation($"Close Price: {closePrice}");

            // log values


            var condition1 = fastEma.Last() > slowEma.Last();
            var condition2 = slowEma.Last() > longEma.Last();
            var condition3 = closePrice > longEma.Last();

            //Price > than Long EMA
            //Long EMA is in an uptrend
            //Fast > Slow EMA
            if ( condition1 && condition2 && condition3) return 1;
            //check if long is trading sideways, need more entries to determin that!

            //check if long is in an uptrend.

            


            return 0;
        }
    }
}
