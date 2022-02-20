using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public abstract class TradingStrategy : ITradingStrategy
    {
        public Dictionary<string, IndicatorSetUp> Indicators { get; }
        public int OutputLength { get; private set; }
        protected abstract double StrategyWeight { get; }
        public ILogger<TradingStrategy> Logger { get; }

        public StringBuilder Builder = new StringBuilder();
        public void Log(string v)
        {
            Builder.Append(v).Append(",\t");
        }
        protected void LogResult(int v)
        {
            Log($"Result: {v}");
            Builder.Append($"Weight: {StrategyWeight}");
            Logger.LogInformation(Builder.ToString());
            Builder.Clear();
        }
        protected bool CheckLastTrade(System.DateTime endDateTime, System.DateTime closeTime, CandlestickInterval interval)
        {
            var nextTradeDate = CandleStickIntervalHelper.NextCandleStickTime(endDateTime, interval);
            return nextTradeDate < closeTime;
        }

        protected TradingStrategy(ILogger<TradingStrategy> logger)
        {
            Logger = logger;
            Indicators = GenerateIndicators();
            int i = 0;
            foreach (var item in Indicators)
            {
                var optionLength = item.Value.Indicator.Start(item.Value.Options);
                if (optionLength == 0) optionLength = Convert.ToInt32(Math.Round(item.Value.Options[0], 0));
                i = Math.Max(optionLength, i);
            }

            OutputLength = i * 2;
            logger.LogInformation($"Strategy Initialisation complete for {this}, output length = {OutputLength}, Strategy Weight = {StrategyWeight}");
        }

        protected abstract Dictionary<string, IndicatorSetUp> GenerateIndicators();

        public virtual double Calculate(CandleStickDictionary closePrices, IStopLimitTracker stopLimitTrackers)
        {
            Dictionary<string, double[][]> indicatorOutputs = new Dictionary<string, double[][]>();
            //load indicators
            foreach (var item in Indicators)
            {
                double[][] inputs = BuildInputs(item.Value,closePrices);                

                //Find output size and allocate output space.
                int output_length = (inputs[0].Length - item.Value.Indicator.Start(item.Value.Options));
                double[] output = new double[output_length];
                double[] output1 = new double[output_length];
                double[] output2 = new double[output_length];
                double[] output3 = new double[output_length];

                double[][] outputs = { output,output1,output2,output3 };
                int success = item.Value.Indicator.Run(inputs, item.Value.Options, outputs);
                // log.
                indicatorOutputs.Add(item.Key, outputs);
            }

            return Calculate(indicatorOutputs, closePrices.Current, stopLimitTrackers) * StrategyWeight;
        }
        //indicators work in reverse order, so the first item is the earliest candlestick.
        private double[][] BuildInputs(IndicatorSetUp indicator, CandleStickDictionary closePrices)
        {
            var candleSticks = closePrices.GroupCandleSticks(indicator.Multiplier);

            double[] close_prices = candleSticks.Select(x => (double)x.Close).ToArray();
            double[] volume = candleSticks.Select(x => (double)x.Volume).ToArray();
            double[] high = candleSticks.Select(x => (double)x.High).ToArray();
            double[] low = candleSticks.Select(x => (double)x.Low).ToArray();

            return new double[][] { close_prices, volume, high, low };
        }

        //return +1 for buy Trade, -1 for sell, and 0 for Hold.
        protected abstract double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers);
    }
}
