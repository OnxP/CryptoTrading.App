using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class MacdOOTradingStrategy : TradingStrategy
    {
        public MacdOOTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }
        public MacdOOTradingStrategy(ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
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
            var macd = new IndicatorSetUp(Tulip.Indicators.macd, new double[] { shortPeriod, longPeriod, signal });

            var sEma = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 9 },5);
            var mEma = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 55 },5);
            var lEma = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 100 },5);
            var rsi = new IndicatorSetUp(Tulip.Indicators.rsi, new double[] { 14, 14, 3, 3 },5);
            var close = new IndicatorSetUp(Tulip.Indicators.close, new double[] { 6 });

            dict.Add("MACD", macd);
            dict.Add("ShortEma", sEma);
            dict.Add("MediumEma", mEma);
            dict.Add("LongEma", lEma);
            dict.Add("close", close);
            dict.Add("Rsi", rsi);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice,
            IStopLimitTracker StopLimitTrackers)
        {
            var hist = indicatorOutputs["MACD"][2].ToList();
            var sEma = indicatorOutputs["ShortEma"][0].ToList();
            var mEma = indicatorOutputs["MediumEma"][0].ToList();
            var longEma = indicatorOutputs["LongEma"][0].ToList();
            var low = indicatorOutputs["close"][3].ToList();
            var atr = indicatorOutputs["atr"][0].ToList();

            var rsi = indicatorOutputs["Rsi"][0].ToList();

            // log values


            //var condition3 = kLine.First() <= 50.0 && kLine.First() >= dLine.First();
            //var condition3 = CheckMacdTail(macd,signal,hist,close);
            var condition1 = sEma.First() < (double)closePrice.Close;
            var condition2 = mEma.First() < sEma.First();
            var condition3 = longEma.First() < mEma.First();
            var condition4 = rsi.First() > 52;



            return StopLimitTrackers.SetStopLimit(indicatorOutputs, closePrice, condition1 && condition4 && condition2,
                s => Logger.LogInformation(s));
        }
    }
}
