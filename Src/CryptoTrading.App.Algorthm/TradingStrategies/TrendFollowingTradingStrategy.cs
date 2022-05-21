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
            dict.Add("shortEma", new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 50 }));
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
            var shortEma = indicatorOutputs["shortEma"][0].ToList();
            var low = indicatorOutputs["close"][3].ToList();

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

            if (condition2 && condition1 && condition3 && condition4 && slCheck)
            {
                SetStopLimit(indicatorOutputs, closePrice, StopLimitTrackers);
                return 1;
            }

            return 0;
        }
    }
}
