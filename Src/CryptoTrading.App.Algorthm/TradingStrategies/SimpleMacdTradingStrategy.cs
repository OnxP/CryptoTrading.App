using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class SimpleMacdTradingStrategy : TradingStrategy
    {
        public SimpleMacdTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        public SimpleMacdTradingStrategy(ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
        {
            noOfTrades = NoOfTrades;
        }

        protected override double StrategyWeight => 1.0 / noOfTrades;
        private double noOfTrades = 1d;

        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            //add indicators to dictionary
            double shortPeriod = 12;
            double longPeriod = 26;
            double signal = 9;
            var macd = new IndicatorSetUp(Tulip.Indicators.macd, new double[] { shortPeriod, longPeriod, signal });

            var ema = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 100 });
            var srsi = new IndicatorSetUp(Tulip.Indicators.stochrsi2, new double[] { 14, 14, 3, 3 });
            var close = new IndicatorSetUp(Tulip.Indicators.close, new double[] { 6 });

            dict.Add("MACD", macd);
            dict.Add("LongEma", ema);
            dict.Add("close", close);
            dict.Add("sRsi", srsi);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice,
            IStopLimitTracker StopLimitTrackers)
        {
            var macd = indicatorOutputs["MACD"][0].ToList();
            var signal = indicatorOutputs["MACD"][1].ToList();
            var hist = indicatorOutputs["MACD"][2].ToList();
            var longEma = indicatorOutputs["LongEma"][0].ToList();
            var low = indicatorOutputs["close"][3].ToList();
            var atr = indicatorOutputs["atr"][0].ToList();

            var kLine = indicatorOutputs["sRsi"][0].ToList();
            var dLine = indicatorOutputs["sRsi"][1].ToList();
            //add rsi and don't make a trade when the rsi is above 60.
            //or remove condition4? or reduce the range
            longEma.Reverse();
            macd.Reverse();
            signal.Reverse();
            hist.Reverse();


            // log values

            var condition1 = macd.First() > signal.First();

            //var condition3 = kLine.First() <= 50.0 && kLine.First() >= dLine.First();
            //var condition3 = CheckMacdTail(macd,signal,hist,close);
            var condition2 = longEma.First() < (double)closePrice.Close;
            var condition4 = macd.First() < 0.0d;



            return StopLimitTrackers.SetStopLimit(indicatorOutputs, closePrice, condition1 && condition4 && condition2,
                s => Logger.LogInformation(s));
        }
    }
}
