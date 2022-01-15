using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tulip;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public abstract class TradingStrategy : ITradingStrategy
    {
        public Dictionary<string, IndicatorSetUp> Indicators { get; }

        public int OutputLength { get; private set; }
        protected abstract double StrategyWeight { get; }
        public ILogger<TradingStrategy> Logger { get; }

        public StringBuilder builder = new StringBuilder();
        public void Log(string v)
        {
            builder.Append(v).Append(",\t");
        }
        protected void LogResult(int v)
        {
            Log($"Result: {v}");
            builder.Append($"Weight: {StrategyWeight}");
            Logger.LogInformation(builder.ToString());
            builder.Clear();
        }
        protected bool CheckLastTrade(System.DateTime endDateTime, System.DateTime closeTime, CandlestickInterval interval)
        {
            var nextTradeDate = CalculateFrom(endDateTime, interval);
            return nextTradeDate < closeTime;
        }

        private DateTime CalculateFrom(DateTime dateTime, CandlestickInterval interval)
        {
            int candleSticksToLoad = -1;
            return interval switch
            {
                CandlestickInterval.Minute => dateTime.AddMinutes(-1 * candleSticksToLoad),
                CandlestickInterval.Minutes_3 => dateTime.AddMinutes(-3 * candleSticksToLoad),
                CandlestickInterval.Minutes_5 => dateTime.AddMinutes(-5 * candleSticksToLoad),
                CandlestickInterval.Minutes_15 => dateTime.AddMinutes(-15 * candleSticksToLoad),
                CandlestickInterval.Minutes_30 => dateTime.AddMinutes(-30 * candleSticksToLoad),
                CandlestickInterval.Hour => dateTime.AddHours(-1 * candleSticksToLoad),
                CandlestickInterval.Hours_2 => dateTime.AddHours(-2 * candleSticksToLoad),
                CandlestickInterval.Hours_4 => dateTime.AddHours(-4 * candleSticksToLoad),
                CandlestickInterval.Hours_6 => dateTime.AddHours(-6 * candleSticksToLoad),
                CandlestickInterval.Hours_8 => dateTime.AddHours(-8 * candleSticksToLoad),
                CandlestickInterval.Hours_12 => dateTime.AddHours(-12 * candleSticksToLoad),
                CandlestickInterval.Day => dateTime.AddDays(-1 * candleSticksToLoad),
                CandlestickInterval.Days_3 => dateTime.AddDays(-3 * candleSticksToLoad),
                CandlestickInterval.Week => dateTime.AddDays(-7 * candleSticksToLoad),
                CandlestickInterval.Month => dateTime.AddMonths(-1 * candleSticksToLoad),
                _ => dateTime,
            };
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
            logger.LogInformation($"Stragegy Initialisation complete for {this}, output length = {OutputLength}, Strategy Weight = {StrategyWeight}");
        }

        protected abstract Dictionary<string, IndicatorSetUp> GenerateIndicators();

        public virtual double Calculate(OrderedFixedLengthList<Candlestick> closePrices, IStopLimitTracker StopLimitTrackers)
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

            return Calculate(indicatorOutputs, closePrices.Current, StopLimitTrackers) * StrategyWeight;
        }

        private double[][] BuildInputs(IndicatorSetUp indicator, OrderedFixedLengthList<Candlestick> closePrices)
        {
            if (indicator.Multiplier == 1)
            {
                double[] close_prices = closePrices.Select(x => (double)x.Close).ToArray();
                double[] volume = closePrices.Select(x => (double)x.Volume).ToArray();
                double[] high = closePrices.Select(x => (double)x.High).ToArray();
                double[] low = closePrices.Select(x => (double)x.Low).ToArray();

                return new double[][] { close_prices, volume, high, low };
            }
            else
            {//fix this..do it in reverse order
                var startCandleStick = closePrices.Last(x => x.OpenTime.Minute == 0);
                var index = closePrices.IndexOf(startCandleStick);
                int size = index / 4;
                double[] close_prices = new double[size];
                double[] volume = new double[size];
                double[] high = new double[size];
                double[] low = new double[size];
                int idx = size-1;
                while (index >= indicator.Multiplier)//might need a -1 here
                {
                    var group = closePrices.Skip(index).Take(indicator.Multiplier);
                    close_prices[idx] = (double)group.Last().Close;
                    volume[idx] = (double)group.Sum(x=>x.Volume);
                    high[idx] = (double)group.Max(x=>x.High);
                    low[idx] = (double)group.Min(x=>x.Low);
                    idx--;
                    index -= indicator.Multiplier;
                }
                return new double[][] { close_prices, volume, high, low };
            }
        }

        //return +1 for buy Trade, -1 for sell, and 0 for Hold.
        protected abstract double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers);
    }
}
