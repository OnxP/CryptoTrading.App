using System;
using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class MacdGradientTradingStrategy : TradingStrategy
    {
        public MacdGradientTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        protected override double StrategyWeight => 1.0;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            //add indicators to dictionary
            //for simple ema strat, we need slow fast and long, 13 21 and 200
            var ema = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 100 });
            var macd = new IndicatorSetUp(Tulip.Indicators.macd, new double[] { 12, 26,9 });
            var psar = new IndicatorSetUp(Tulip.Indicators.psar, new double[] { 0.02, 0.2 });
            var bbands = new IndicatorSetUp(Tulip.Indicators.bbands, new double[] { 100, 2 });
            var close = new IndicatorSetUp(Tulip.Indicators.close, new double[] { 10 });
            var adx = new IndicatorSetUp(Tulip.Indicators.adx, new double[] { 14 });
            dict.Add("ema", ema);
            dict.Add("macd", macd);
            dict.Add("bbands", bbands);
            dict.Add("psar", psar);
            dict.Add("close", close);
            dict.Add("adx", adx);
            return dict;
        }

        public bool newTradingOppertunity = false;
        public double lastSellPsar;

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var ema = indicatorOutputs["ema"][0].ToList();
            var macd = indicatorOutputs["macd"][0].ToList();
            var signal = indicatorOutputs["macd"][1].ToList();
            var hist = indicatorOutputs["macd"][2].ToList();
            var adx = indicatorOutputs["adx"][0].ToList();
            //close is already in order.
            var close = indicatorOutputs["close"][0].ToList();

            ema.Reverse();
            var condition1 = macd.Last() > signal.Last();
            
            //macd should move out of a range specifically around the 1SD over the sma of the +ive hist points.
            var condition4 = hist.Last() > MacdBBand(hist,20,1,true);


            var condition2 = ema.First() < (double)closePrice.Close;
            var condition3 = ema.First() - ema.Take(10).Average() > 0;
            var condition5 = adx.Last() >= 25;
            
            //set a tighter stop loss if macd line is +ive and looser sl if -ive
            //psar
            return StopLimitTrackers.SetStopLimit(indicatorOutputs, closePrice, condition1 && condition2 && condition3 && condition4 && condition5,s => Logger.LogInformation(s));
        }

        private double MacdBBand(List<double> hist, int smaLength, double stdDev, bool positivePoints)
        {
            var points = hist.Where(x => positivePoints ? x > 0 : x < 0);

            List<double> result = new List<double>();

            while (points.Count() >= result.Count() + smaLength)
            {
                var sum = points.Skip(result.Count).Take(smaLength).Sum(x => x * x) / smaLength;
                var sum2 = points.Skip(result.Count).Take(smaLength).Sum() / smaLength;
                var sd = Math.Sqrt(sum - Math.Pow(sum2,2));


                result.Add(sum2  + (positivePoints ? 1 : -1) * sd * stdDev);
            }

            return result.Last();
        }
    }
}
