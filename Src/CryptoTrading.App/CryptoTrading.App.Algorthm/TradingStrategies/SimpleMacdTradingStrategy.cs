using Binance;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public class SimpleMacdTradingStrategy : TradingStrategy
    {
        public SimpleMacdTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
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
            var ema = (Tulip.Indicators.ema, new double[] { 200 });
            var vwap = (Tulip.Indicators.vwap, new double[] { 0 });

            dict.Add("MACD", macd);
            dict.Add("LongEma", ema);
            dict.Add("VWap", vwap);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice)
        {
            var macd = indicatorOutputs["MACD"][0].ToList();
            var signal = indicatorOutputs["MACD"][1].ToList();
            var hist = indicatorOutputs["MACD"][2].ToList();
            var longEma = indicatorOutputs["LongEma"][0].ToList();
            var vWap = indicatorOutputs["VWap"][0].ToList();

            longEma.Reverse();
            macd.Reverse();
            signal.Reverse();

            Log($"MACD - Line: {macd.First()}");
            Log($"MACD - Signal Line: {signal.First()}");
            Log($"Long Ema: {longEma.First()}");
            Log($"Close Price: {closePrice}");

            // log values
            
            var condition1 = macd.First() >= signal.First();
            var condition3= macd.Skip(1).First() <= signal.Skip(1).First();
            var condition2 = vWap.First() < (double)closePrice.Close; //this checks for an up trend however doesn't check for sideways trend.
            //var condition3 = vWap.First() > longEma.Skip(1).First(); //this checks for an up trend however doesn't check for sideways trend.
            //var condition4 = macd.First() > macd.Skip(1).First();
            //var condition5 = macd.First() < 0;
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
