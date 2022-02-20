using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class BasicMacd : TradingStrategy
    {
        public BasicMacd(ILogger<TradingStrategy> logger) : base(logger)
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
            var macd = new IndicatorSetUp(Tulip.Indicators.macd, new double[] { shortPeriod, longPeriod, signal });
            var ema = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 200 });
            var rsi = new IndicatorSetUp(Tulip.Indicators.rsi, new double[] { 14 });
            var sRsi = new IndicatorSetUp(Tulip.Indicators.stochrsi, new double[] { 14 });
            dict.Add("Rsi", rsi);
            dict.Add("SRsi", sRsi);
            dict.Add("MACD", macd);
            dict.Add("LongEma", ema);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var macd = indicatorOutputs["MACD"][0].ToList();
            var signal = indicatorOutputs["MACD"][1].ToList();
            var hist = indicatorOutputs["MACD"][2].ToList();
            var longEma = indicatorOutputs["LongEma"][0].ToList();
            var rsi = indicatorOutputs["Rsi"][0].ToList();
            var sRsi = indicatorOutputs["SRsi"][0].ToList();

            var dLine = new List<double>();

            for (int i = 0; i <= sRsi.Count() - 3; i++)
                dLine.Add(sRsi.Skip(i).Take(3).Average());

            longEma.Reverse();
            macd.Reverse();

            Log($"MACD - Line: {macd.First()}");
            Log($"MACD - Signal Line: {signal.Last()}");
            Log($"Long Ema: {longEma.First()}");
            Log($"Close Price: {closePrice}");

            // log values

            var condition1 = macd.First() >= signal.Last();
            var condition2 = longEma.First() < (double)closePrice.Close; //this checks for an up trend however doesn't check for sideways trend.
            var condition3 = longEma.First() > longEma.Skip(1).First(); //this checks for an up trend however doesn't check for sideways trend.
            var condition4 = macd.First() > macd.Skip(1).First();
            //var condition5 = sRsi.Last() >= dLine.Last();
            //var condition6 = rsi.Last() <= 60;
            //var condition7 = sRsi.Last() <= 60;
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
