using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class MacdRSITradingStrategy : TradingStrategy
    {
        public MacdRSITradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }
        public MacdRSITradingStrategy(ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
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
            var macd = new IndicatorSetUp(Tulip.Indicators.macd, new double[] { shortPeriod, longPeriod, signal },4);
            var ema100 = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 100 });
            var ema50 = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 50 });
            var ema25 = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 25 });
            var srsi = new IndicatorSetUp(Tulip.Indicators.stochrsi2, new double[] { 14, 14, 3, 3 });
            var close = new IndicatorSetUp(Tulip.Indicators.close, new double[] { 10 });
            var close1 = new IndicatorSetUp(Tulip.Indicators.close, new double[] { 10 },4);
            dict.Add("MACD", macd);
            dict.Add("MediumWma", ema50);
            dict.Add("longWma", ema100);
            dict.Add("ShortWma", ema25);
            dict.Add("SRSI", srsi);
            dict.Add("close", close);
            dict.Add("closehr", close1);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var macd = indicatorOutputs["MACD"][0].ToList();
            var signal = indicatorOutputs["MACD"][1].ToList();
            var hist = indicatorOutputs["MACD"][2].ToList();
            var shortWma = indicatorOutputs["ShortWma"][0].ToList();
            var mediumWma = indicatorOutputs["MediumWma"][0].ToList();
            var kLine = indicatorOutputs["SRSI"][0].ToList();
            var dLine = indicatorOutputs["SRSI"][1].ToList();
            var atr = indicatorOutputs["atr"][0].ToList();
            var volume = indicatorOutputs["close"][1].ToList();
            var volumeHr = indicatorOutputs["closehr"][1].ToList();

            macd.Reverse();
            signal.Reverse();

            var macdCondition1 = macd.First() >= signal.First();
            var macdCondition2 = macd.First() <= 0;
            var volumeCondition = volumeHr.First() > volumeHr.Skip(1).First();


            var condition3 = mediumWma.Last() <= (double)closePrice.Low;
            var condition4 = shortWma.Last() >= mediumWma.Last();


            var volumeCondition2 = volume.First() > volume.Skip(1).First();

            var condition5 = kLine.Last() >= dLine.Last();
            var condition6 = kLine.Last() <= 80 && kLine.Last() >= 20;
            var condition9 = kLine.Last() >= 80 ;
            var condition7 = (double)closePrice.Close < mediumWma.Last();
            var condition8 = (double)closePrice.Close > mediumWma.Last();

            var multiple = Convert.ToDecimal(atr.Last());
            //var percentOfPrice = 100 - (((closePrice.Close - multiple) / closePrice.Close) * 100); 
            var highvol = StopLimitTrackers.Risk < 2 * (multiple / closePrice.Close);


            return StopLimitTrackers.SetStopLimit(indicatorOutputs, closePrice,
                macdCondition1 && volumeCondition && condition5 && !highvol &&
                condition4 && condition3 && ((condition7 && condition9) || (condition8 && condition6)),
                s => Logger.LogInformation(s));
        }

    }
}
