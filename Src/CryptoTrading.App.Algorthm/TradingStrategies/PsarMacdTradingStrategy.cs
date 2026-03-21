using System;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class PsarMacdTradingStrategy : TradingStrategy
    {
        public PsarMacdTradingStrategy(ILogger<TradingStrategy> logger, ISymbolCache symbolCache) : base(logger, symbolCache)
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

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var ema = indicatorOutputs["ema"][0].ToList();
            var macd = indicatorOutputs["macd"][0].ToList();
            var signal = indicatorOutputs["macd"][1].ToList();
            var hist = indicatorOutputs["macd"][2].ToList();
            var psar = indicatorOutputs["psar"][0].ToList();
            var adx = indicatorOutputs["adx"][0].ToList();
            var upper = indicatorOutputs["bbands"][0].ToList();
            var lower = indicatorOutputs["bbands"][1].ToList();
            var middle = indicatorOutputs["bbands"][2].ToList();
            //close is already in order.
            var close = indicatorOutputs["close"][0].ToList();

            //need to exclude massive candles if the diff Abs(close-open) > higher - lower bbands
            var diff = closePrice.Close - closePrice.Open;
            if (diff / closePrice.Close > 0.05m) return 0;
            hist.Reverse();
            if (macd.Last() > 0 && close.First() > upper.Last()) return 0;
            //strat
            //1.wait for the first buy signal from the psar and set the previous
            psar.Reverse();
            if (psar.First() < close.First() && psar.Skip(1).First() > close.Skip(1).First() )
            {
                //psar change indicating a new trading opp...
                //save the value for the previous psar, once price breaks this then its a trading oppertuinity.
                newTradingOppertunity = true;
                lastSellPsar = psar.Skip(1).First();
            }

            var check1 = psar.First() > close.First();
            var check2 = lastSellPsar < close.Skip(1).First();

            if (check1 || check2 ) newTradingOppertunity = false;

            if (!newTradingOppertunity || !(lastSellPsar < close.First())) return 0;
            //once the price has been breached check the macd and EMA for confirmation

            var condition1 = macd.Last() > signal.Last();
            var condition2 = ema.Last() < close.First();
            var condition3 = adx.Last() > 25;
            var condition4 = hist.Last() > MacdBBand(hist, 20, 1, true);
            newTradingOppertunity = false;
            
            //set a tighter stop loss if macd line is +ive and looser sl if -ive
            //psar
            return StopLimitTrackers.SetStopLimit(indicatorOutputs, closePrice, condition1 && condition2 && condition3 && condition4,s => Logger.LogInformation(s));
        }

        private double MacdBBand(List<double> hist, int smaLength, double stdDev, bool positivePoints)
        {
            var points = hist.Where(x => positivePoints ? x > 0 : x < 0);

            List<double> result = new List<double>();

            while (points.Count() >= result.Count() + smaLength)
            {
                var sum = points.Skip(result.Count).Take(smaLength).Sum(x => x * x) / smaLength;
                var sum2 = points.Skip(result.Count).Take(smaLength).Sum() / smaLength;
                var sd = Math.Sqrt(sum - Math.Pow(sum2, 2));


                result.Add(sum2 + (positivePoints ? 1 : -1) * sd * stdDev);
            }

            return result.Last();
        }
    }
}
