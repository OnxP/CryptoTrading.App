using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class BBEMATradingStrategy : TradingStrategy
    {
        public BBEMATradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        public BBEMATradingStrategy(ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
        {
            noOfTrades = NoOfTrades;
        }

        protected override double StrategyWeight => 1.0 / noOfTrades;
        private double noOfTrades = 1d;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            var bbands = new IndicatorSetUp(Tulip.Indicators.bbands, new double[] { 20,2 });
            var ema = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 100 });
            var sema = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 9 });
            dict.Add("close", new IndicatorSetUp(Tulip.Indicators.close, new double[] { 6 }));
            dict.Add("BBands", bbands);
            dict.Add("LongEma", ema);
            dict.Add("Ema", sema);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var lower = indicatorOutputs["BBands"][0].ToList();
            var middle = indicatorOutputs["BBands"][1].ToList();
            var upper = indicatorOutputs["BBands"][2].ToList();
            var longEma = indicatorOutputs["Ema"][0].ToList();
            var close = indicatorOutputs["close"][0];

            // log values
            lower.Reverse();
            var condition1 = close[1] < lower.Skip(1).First();
            var condition2 = close[0] > lower.First();
            var condition3 = closePrice.Open <= closePrice.Close;
            var potentialprofit = ((Convert.ToDecimal(longEma.Last()) - closePrice.Close) / closePrice.Close) * 100;
            var condition4 = potentialprofit > 0.04m;

            var potentialSt = ((closePrice.Close - closePrice.Low) / closePrice.Low)+1.0m - 0.005m;
            var stoploss = Math.Min(closePrice.Low, potentialSt * closePrice.Close);
            //if the low is too large

            //too things needs to be done here, excluded ones that are in a down trend, ranging or uptrends ones are ok
            //change the stop limit to be wider, then when the target is hit move the stop limit to the buy + fee.
            //then trail the pricebuy the difference.

            //Price > than Long EMA
            //Long EMA is in an uptrend
            //Fast > Slow EMA
            if (condition1 && condition2 && condition3 && condition4)
            {
                SetStopLimit(indicatorOutputs, closePrice, StopLimitTrackers);
                return 1;
            }
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
