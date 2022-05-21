using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class MacdGmaTradingStrategy : TradingStrategy
    {
        public MacdGmaTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }
        public MacdGmaTradingStrategy(ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
        {
            noOfTrades = NoOfTrades;
        }
        protected override double StrategyWeight => 1.0 / noOfTrades;
        private double noOfTrades = 1d;
        //protected override double StrategyWeight => 1.0;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            //add indicators to dictionary
            double shortPeriod = 12;
            double longPeriod = 26;
            double signal = 9;
            var macd = new IndicatorSetUp(Tulip.Indicators.macd, new double[] { shortPeriod, longPeriod,signal });
            var ema100 = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 100 });
            var ema5 = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 5 });
            var ema20 = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 20 });
            var gema20 = new IndicatorSetUp(Tulip.Indicators.gema, new double[] { 20,10 });
            var srsi = new IndicatorSetUp(Tulip.Indicators.stochrsi2, new double[] { 14,14,3,3 });
            var rsi = new IndicatorSetUp(Tulip.Indicators.rsi, new double[] { 14 });
            dict.Add("MACD", macd);
            dict.Add("LongWma", ema100);
            dict.Add("MediumWma", ema20);
            dict.Add("ShortWma", ema5);
            dict.Add("gema", gema20);
            dict.Add("SRSI", srsi);
            dict.Add("rsi", rsi);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var macd = indicatorOutputs["MACD"][0].ToList();
            var signal = indicatorOutputs["MACD"][1].ToList();
            var hist = indicatorOutputs["MACD"][2].ToList();
            var shortWma = indicatorOutputs["ShortWma"][0].ToList();
            var gema = indicatorOutputs["gema"][0].ToList();
            var longWma = indicatorOutputs["LongWma"][0].ToList();
            var mediumWma = indicatorOutputs["MediumWma"][0].ToList();
            var kLine = indicatorOutputs["SRSI"][0].ToList();
            var dLine = indicatorOutputs["SRSI"][1].ToList();
            var rsi = indicatorOutputs["rsi"][0].ToList();


            var condition1 = macd.Last() > signal.Last();
            var condition2 = shortWma.Last() >= mediumWma.Last();
            var condition3 = kLine.Last() >= dLine.Last();
            var condition4 = rsi.Last() < 50;
            var condition5 = gema.Last() >0;

            //Price > than Long EMA
            //Long EMA is in an uptrend
            //Fast > Slow EMA
             if (condition1 && condition2 && condition3 && condition5)
            {
                SetStopLimit(indicatorOutputs, closePrice, StopLimitTrackers);
                return 1;
            }
             return 0;
        }

        private bool EmaBounce(List<double> wma, Candlestick closePrice)
        {
            if (lastClose == null)
            {
                lastClose = closePrice;
                return false;
            }
            if (wma.Count() == 1)
            {
                return false;
            }
            var bounce = (double)closePrice.Low < wma.First() && (double)closePrice.Close > wma.First();
            var previousPassThrough = (double)lastClose.Close < wma.Skip(1).First() && (double)lastClose.Open > wma.Skip(1).First();
            lastClose = closePrice;
            return bounce && previousPassThrough;
        }
        public Candlestick lastClose;
    }
}
