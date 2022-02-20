using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class EmaRsiTradingStrategy : TradingStrategy
    {
        public EmaRsiTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        public EmaRsiTradingStrategy(ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
        {
            noOfTrades = NoOfTrades;
        }

        protected override double StrategyWeight => 1.0 / noOfTrades;
        private double noOfTrades = 1d;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            var ema8 = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 8 });
            var ema14 = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 14 });
            var ema50 = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 50 });
            var srsi = new IndicatorSetUp(Tulip.Indicators.stochrsi2, new double[] { 14 , 14 ,3 ,3 });
            var atr = new IndicatorSetUp(Tulip.Indicators.atr, new double[] { 14 });
            dict.Add("close", new IndicatorSetUp(Tulip.Indicators.close, new double[] { 6 }));

            dict.Add("sRsi", srsi);
            dict.Add("Atr", atr);
            dict.Add("LongEma", ema50);
            dict.Add("MediumEma", ema14);
            dict.Add("ShortEma", ema8);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var longEma = indicatorOutputs["LongEma"][0].ToList();
            var mediumEma = indicatorOutputs["MediumEma"][0].ToList();
            var shortEma = indicatorOutputs["ShortEma"][0].ToList();
            var atr = indicatorOutputs["Atr"][0].ToList();
            var kLine = indicatorOutputs["sRsi"][0].ToList();
            var dLine = indicatorOutputs["sRsi"][1].ToList();
            var close = indicatorOutputs["close"][0];

            // log values

            var condition1 = shortEma.Last() > mediumEma.Last() && mediumEma.Last() > longEma.Last();
            var condition2 = Convert.ToDouble(closePrice.Close) > shortEma.Last();
            var condition3 = kLine.Last() >= dLine.Last();

            //Price > than Long EMA
            //Long EMA is in an uptrend
            //Fast > Slow EMA

            if (!StopLimitTrackers.IsOpen)
            {
                StopLimitTrackers.Increment = Convert.ToDecimal((atr.Last()));
            }
            if (condition1 && condition2 && condition3)
            {
                LogResult(1);
                StopLimitTrackers.StopLimitPrice = closePrice.Close - Convert.ToDecimal((atr.Last() * 3));
                StopLimitTrackers.TargetPrice = closePrice.Close + Convert.ToDecimal((atr.Last()));
                return StrategyWeight;
            }
            //check if long is trading sideways, need more entries to determin that!

            //check if long is in an uptrend.
            LogResult(0);
            return 0;
        }
        private bool LastSixClose(double[] close)
        {
            if (close[1] < close[2] && close[2] < close[3] && close[3] < close[4] && close[4] < close[5] && close[5] < close[6])
                return true;
            else if (close[1] < close[2] && close[2] < close[3] && close[3] < close[4])
                return true;
            else if (close[2] < close[3] && close[3] < close[4] && close[4] < close[5] && close[5] < close[6])
                return true;
            else if (close[1] < close[2] && close[3] < close[4] && close[4] < close[5] && close[5] < close[6])
                return true;
            else if (close[1] < close[2] && close[2] < close[3] && close[4] < close[5] && close[5] < close[6])
                return true;
            else if (close[1] < close[2] && close[2] < close[3] && close[3] < close[4] && close[5] < close[6])
                return true;
            else if (close[1] < close[2] && close[2] < close[3] && close[3] < close[4] && close[4] < close[5])
                return true;
            else return false;
        }

    }
}
