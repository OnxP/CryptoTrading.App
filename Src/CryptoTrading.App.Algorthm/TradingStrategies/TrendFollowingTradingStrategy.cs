using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class TrendFollowingTradingStrategy : TradingStrategy
    {
        public TrendFollowingTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }


        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            dict.Add("MACD", new IndicatorSetUp(Tulip.Indicators.macd, new double[] { 12,26,9}));
            dict.Add("WillR", new IndicatorSetUp(Tulip.Indicators.willr, new double[] { 14}));
            dict.Add("LongEma", new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 100 }));
            dict.Add("MediumEma", new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 50 }));
            dict.Add("shortEma", new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 25 }));
            dict.Add("close", new IndicatorSetUp(Tulip.Indicators.close, new double[] { 20 }));
            dict.Add("sRsi", new IndicatorSetUp(Tulip.Indicators.stochrsi2, new double[] { 14, 14, 3, 3 }));
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice,
            IStopLimitTracker StopLimitTrackers)
        {
            var macd = indicatorOutputs["MACD"][0].ToList();
            var signal = indicatorOutputs["MACD"][1].ToList();
            var hist = indicatorOutputs["MACD"][2].ToList();
            var longEma = indicatorOutputs["LongEma"][0].ToList();
            var mediumEma = indicatorOutputs["MediumEma"][0].ToList();
            var shortEma = indicatorOutputs["shortEma"][0].ToList();
            var low = indicatorOutputs["close"][3].ToList();

            //look at al 3 moving averages and they need to stack,diverging and have a sutible grediant
            //this is key to identify the uptrend, the long EMA will make sure its out of a range market and the short EMA will stop it from entering a ranged market.
            //the problem here is that the 25 EMA's gradient will drop when there is a good trading oppertunity.
            //sop once found then the the window will open, other indicators will supply the entry signal.
            //Note that the the gradient of the 25 ema may cause the window to close while waiting for the lowest point,
            //which is fine as long as the gradient of the 50 and 100.

            //best times to get into a trade is when the price bounces off the 25, 50 or 100 emas

            var isUpTrend = IsInUpTrend(shortEma,mediumEma,longEma);


            var kLine = indicatorOutputs["sRsi"][0].ToList();
            var dLine = indicatorOutputs["sRsi"][1].ToList();

            //Identify Long term trend, making sure it is in an uptrend.
            //based on price action the peaks should be making higher higs and higher lows.
            //need to programically find the 3 highs and last 3 lows. this is all based on close price.


            var condition1 = macd.Last() > signal.Last();
            var condition2 = longEma.Last() < (double)closePrice.Close;
            var condition3 = shortEma.Last() < (double)closePrice.Close;
            var condition4 = macd.Last() < 0.0d;

            //stop loss check
            var slCheck = (closePrice.Close * (1-StopLimitTrackers.Risk)) >
                          Symbol.Cache.Get(closePrice.Symbol).Price.Increment*2;


            return StopLimitTrackers.SetStopLimit(indicatorOutputs, closePrice, condition2 && condition1 && condition3 && condition4 && slCheck,
                s => Logger.LogInformation(s));
        }

        private object IsInUpTrend(List<double> shortEma, List<double> mediumEma, List<double> longEma)
        {
            throw new System.NotImplementedException();
        }
    }
}
