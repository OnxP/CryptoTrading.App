using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public class MacdTradingStrategy : TradingStrategy
    {
        public MacdTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        protected override double StrategyWeight => 1.0;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, (Indicator indicator, double[] options)> GenerateIndicators()
        {
            var dict = new Dictionary<string, (Indicator indicator, double[] options)>();

            //add indicators to dictionary
            double shortPeriod = 12;
            double longPeriod = 26;
            double signal = 9;
            var macd = (Tulip.Indicators.macd, new double[] { shortPeriod, longPeriod,signal });
            var ema = (Tulip.Indicators.ema, new double[] { 100 });
            dict.Add("MACD", macd);
            dict.Add("LongEma", ema);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, double closePrice)
        {
            var macd = indicatorOutputs["MACD"][0].ToList();
            var signal = indicatorOutputs["MACD"][1].ToList();
            var hist = indicatorOutputs["MACD"][2].ToList();
            var longEma = indicatorOutputs["LongEma"][0].ToList();

            longEma.Reverse();

            Log($"MACD - Line: {macd.Last()}");
            Log($"MACD - Signal Line: {signal.Last()}");
            Log($"Long Ema: {longEma.First()}");
            Log($"Close Price: {closePrice}");

            // log values
            
            var condition1 = macd.Last() > signal.Last();
            var condition2 = longEma.First() < closePrice; //this checks for an up trend however doesn't check for sideways trend.
            var condition3 = longEma.First() > longEma.Skip(1).First(); //this checks for an up trend however doesn't check for sideways trend.
            //Price > than Long EMA
            //Long EMA is in an uptrend
            //Fast > Slow EMA
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
