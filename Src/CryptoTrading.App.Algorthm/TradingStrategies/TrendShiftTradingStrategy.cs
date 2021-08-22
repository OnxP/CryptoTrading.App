using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public class TrendShiftTradingStrategy : TradingStrategy
    {
        public TrendShiftTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
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
            var rsi = (Tulip.Indicators.rsi, new double[] { 14 });
            var adx = (Tulip.Indicators.adx, new double[] { 14, 14 });
            var srsi = (Tulip.Indicators.stochrsi, new double[] { 14 });
            var vwap = (Tulip.Indicators.vwap, new double[] { 0 });



            dict.Add("MACD", macd);
            dict.Add("RSI", rsi);
            dict.Add("ADX", adx);
            dict.Add("VWAP", vwap);
            dict.Add("SRSI", srsi);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var macd = indicatorOutputs["MACD"][0].ToList();
            var signal = indicatorOutputs["MACD"][1].ToList();
            var hist = indicatorOutputs["MACD"][2].ToList();
            var rsi = indicatorOutputs["RSI"][0].ToList();
            var adx = indicatorOutputs["ADX"][0].ToList();
            var vwap = indicatorOutputs["VWAP"][0].ToList();
            var srsi = indicatorOutputs["SRSI"][0].ToList();

            adx.Reverse();
            var kLine = new List<double>();

            for (int i = 0; i <= srsi.Count() - 3; i++)
                kLine.Add(srsi.Skip(i).Take(3).Average());

            var dLine = new List<double>();

            for (int i = 0; i <= kLine.Count() - 3; i++)
                dLine.Add(kLine.Skip(i).Take(3).Average());

            Log($"MACD - Line: {macd.First()}");
            Log($"MACD - Signal Line: {signal.First()}");
            Log($"Close Price: {closePrice}");

            //Up Trend Conditions

            var condition1 = macd.Last() >= signal.Last();
            var condition3 = rsi.Last() > 60;
            //var condition2 = kLine.Last() < 0.8;
            var condition4 = kLine.Last() > dLine.Last();
            var condition5 = adx.First() > 20;
            var condition6 = adx.First() > adx.Skip(1).First();
            var condition7 = adx.Skip(1).First() > adx.Skip(2).First();
            if (condition1 && condition3 && condition4 )
            {
                if (condition5 && condition6 && condition7)
                {
                    LogResult(1);
                    return 1;
                }

                if(closePrice.Close > (decimal)vwap.Last() && adx.First() > 20)
                {
                    LogResult(1);
                    return 1;
                }
            }
            //check if long is trading sideways, need more entries to determin that!

            //check if long is in an uptrend.
            LogResult(0);
            return 0;
        }

        
    }
}
