using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public abstract class TradingStrategy : ITradingStrategy
    {
        public Dictionary<string, (Indicator indicator, double[] options)> Indicators { get; }

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

        protected TradingStrategy(ILogger<TradingStrategy> logger)
        {
            Logger = logger;
            Indicators = GenerateIndicators();
            int i = 0;
            foreach (var item in Indicators)
            {
                var optionLength = item.Value.indicator.Start(item.Value.options);
                if (optionLength == 0) optionLength = Convert.ToInt32(Math.Round(item.Value.options[0], 0));
                i = Math.Max(optionLength, i);
            }

            OutputLength = i;
            logger.LogInformation($"Stragegy Initialisation complete for {this}, output length = {OutputLength}, Strategy Weight = {StrategyWeight}");
        }

        protected abstract Dictionary<string, (Indicator indicator, double[] options)> GenerateIndicators();

        public double Calculate(OrderedFixedLengthList<Candlestick> closePrices)
        {
            Dictionary<string, double[][]> indicatorOutputs = new Dictionary<string, double[][]>();
            //load indicators
            foreach (var item in Indicators)
            {
                double[] close_prices = closePrices.Select(x=> (double)x.Close).ToArray();
                double[] volume = closePrices.Select(x => (double)x.Volume).ToArray(); 

                //Find output size and allocate output space.
                int output_length = close_prices.Length - item.Value.indicator.Start(item.Value.options);
                double[] output = new double[output_length];
                double[] output1 = new double[output_length];
                double[] output2 = new double[output_length];

                double[][] inputs = { close_prices, volume };
                double[][] outputs = { output,output1,output2 };
                int success = item.Value.indicator.Run(inputs, item.Value.options, outputs);
                // log.
                indicatorOutputs.Add(item.Key, outputs);
            }

            return Calculate(indicatorOutputs, closePrices.Current) * StrategyWeight;
        }
        //return +1 for buy Trade, -1 for sell, and 0 for Hold.
        protected abstract double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice);
    }
}
