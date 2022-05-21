using System;
using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class TrendMeterTradingStrategy : TradingStrategy
    {
        public TrendMeterTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }
        public TrendMeterTradingStrategy(ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
        {
            noOfTrades = NoOfTrades;
        }
        protected override double StrategyWeight => 1.0 / noOfTrades;
        private double noOfTrades = 1d;
        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();
            dict.Add("MACD",new IndicatorSetUp(Tulip.Indicators.macd,new double[]{8,12,5}));
            dict.Add("RSI",new IndicatorSetUp(Tulip.Indicators.rsi,new double[]{14}));
            dict.Add("EMA5",new IndicatorSetUp(Tulip.Indicators.ema,new double[]{5}));
            dict.Add("EMA11",new IndicatorSetUp(Tulip.Indicators.ema,new double[]{11}));
            dict.Add("EMA13",new IndicatorSetUp(Tulip.Indicators.ema,new double[]{13}));
            dict.Add("EMA50",new IndicatorSetUp(Tulip.Indicators.ema,new double[]{50}));
            dict.Add("EMA200",new IndicatorSetUp(Tulip.Indicators.ema,new double[]{200}));
            dict.Add("SMA36",new IndicatorSetUp(Tulip.Indicators.sma,new double[]{36}));
            dict.Add("close", new IndicatorSetUp(Tulip.Indicators.close, new double[] { 100 }));
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var macd = indicatorOutputs["MACD"][0].ToList();
            var signal = indicatorOutputs["MACD"][1].ToList();
            var hist = indicatorOutputs["MACD"][2].ToList();
            var ema5 = indicatorOutputs["EMA5"][0].ToList();
            var ema11 = indicatorOutputs["EMA11"][0].ToList();
            var ema13 = indicatorOutputs["EMA13"][0].ToList();
            var ema50 = indicatorOutputs["EMA50"][0].ToList();
            var ema200 = indicatorOutputs["EMA200"][0].ToList();
            var sma36 = indicatorOutputs["SMA36"][0].ToList();
            var rsi = indicatorOutputs["RSI"][0].ToList();
            var close = indicatorOutputs["close"][0].ToList();
            var open = indicatorOutputs["close"][4].ToList();

            var condition1 = macd.Last() - signal.Last() > 0;
            var condition2 = rsi.Last() < 50 && rsi.Last() > 5;
            var condition3 = ema11.Last() < ema5.Last();
            var condition4 = sma36.Last() < ema13.Last();
            var condition5 = ema200.Last() < ema50.Last();
            var condition6 = (closePrice.Close - closePrice.Open) > (decimal)StandardDeviation(close,open,100);

            var slCheck = (decimal)indicatorOutputs["atr"][0].ToList().Last() >
                          Symbol.Cache.Get(closePrice.Symbol).Price.Increment * 2;

            if (condition1 && condition2 && condition3 && condition4 && condition5 && slCheck)
            {
                SetStopLimit(indicatorOutputs, closePrice, StopLimitTrackers);
                return 1;
            }
            return 0;
        }

        public double StandardDeviation(List<double> close, List<double> open, double mean)
        {
            double standardDeviation = 0;

            var values = new List<double>();

            for (int i = close.Count() - 1; i >= 0; i--)
            {
                if (close[i] == 0) continue;
                values.Add(close[i]-open[i]);
            }

            if (values.Any())
            {
                // Compute the average.     
                double avg = values.Average();

                // Perform the Sum of (value-avg)_2_2.      
                double sum = values.Sum(d => Math.Pow(d - mean, 2));

                // Put it all together.      
                standardDeviation = Math.Sqrt((sum) / (values.Count() - 1));
            }

            return standardDeviation;
        }
    }
}
