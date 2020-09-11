using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public abstract class TradingStrategy : ITradingStrategy
    {
        public Dictionary<string, (Indicator indicator, double[] options)> Indicators { get; }

        public int OutputLength { get; private set; }
        protected abstract double StrategyWeight { get; }

        public TradingStrategy()
        {
            Indicators = GenerateIndicators();
            int i = 0;
            foreach (var item in Indicators)
            {
                i = Math.Max(item.Value.indicator.Start(item.Value.options),i);
            }

            OutputLength = i;
        }

        protected abstract Dictionary<string, (Indicator indicator, double[] options)> GenerateIndicators();

        public double Calculate(OrderedFixedLengthList closePrices)
        {
            Dictionary<string, double[][]> indicatorOutputs = new Dictionary<string, double[][]>();
            //load indicators
            foreach (var item in Indicators)
            {
                double[] close_prices = closePrices.ToArray();

                //Find output size and allocate output space.
                int output_length = close_prices.Length - item.Value.indicator.Start(item.Value.options);
                double[] output = new double[output_length];

                double[][] inputs = { close_prices };
                double[][] outputs = { output };
                int success = item.Value.indicator.Run(inputs, item.Value.options, outputs);
                // log.
                indicatorOutputs.Add(item.Key, outputs);
            }

            return Calculate(indicatorOutputs, closePrices.Current) * StrategyWeight;
        }
        //return +1 for buy Trade, -1 for sell, and 0 for Hold.
        protected abstract double Calculate(Dictionary<string, double[][]> indicatorOutputs, double closePrice);
    }
}
