using Binance;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public class BBRsiTradingStrategy : TradingStrategy
    {
        public BBRsiTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        protected override double StrategyWeight => 1.0;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, (Indicator indicator, double[] options)> GenerateIndicators()
        {
            var dict = new Dictionary<string, (Indicator indicator, double[] options)>();

            var bbands = (Tulip.Indicators.bbands, new double[] { 20,2 });
            var ema = (Tulip.Indicators.ema, new double[] { 100 });
            var srsi = (Tulip.Indicators.stochrsi2, new double[] { 14 , 14 ,3 ,3 });
            var rsi = (Tulip.Indicators.rsi, new double[] { 14 });
            dict.Add("sRsi", srsi);
            dict.Add("Rsi", rsi);
            dict.Add("BBands", bbands);
            dict.Add("LongEma", ema);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice)
        {
            var lower = indicatorOutputs["BBands"][0].ToList();
            var middle = indicatorOutputs["BBands"][1].ToList();
            var upper = indicatorOutputs["BBands"][2].ToList();
            var longEma = indicatorOutputs["LongEma"][0].ToList();
            var rsi = indicatorOutputs["Rsi"][0].ToList();
            var kLine = indicatorOutputs["sRsi"][0].ToList();
            var dLine = indicatorOutputs["sRsi"][1].ToList();

            longEma.Reverse();

            Log($"BBands - Lower: {lower.Last()}");
            Log($"BBands - Middle: {middle.Last()}");
            Log($"BBands - Upper: {upper.Last()}");
            Log($"Long Ema: {longEma.First()}");
            Log($"Rsi: {rsi.Last()}");
            Log($"SRSI - K: {kLine.Last()}");
            Log($"SRSI - D: {dLine.Last()}");
            Log($"Close Price: {closePrice}");

            // log values
            
            var condition1 = (double)closePrice.Low <= lower.Last();
            var condition2 = rsi.Last() <= 40;
            var condition3 = kLine.Last() >= dLine.Last();
            var condition4 = kLine.Last() <= 30;
            //Price > than Long EMA
            //Long EMA is in an uptrend
            //Fast > Slow EMA
            if (condition1 && condition2 && condition3 && condition4)
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
