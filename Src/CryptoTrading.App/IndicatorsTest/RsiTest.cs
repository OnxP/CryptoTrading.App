using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tulip;

namespace Ankur.Trading.Test.IndicatorsTests
{
    [TestClass]
    public class RsiTest
    {
        private static IEnumerable<double> BuildCandleSticks()
        {
            var list = new List<double>
            {
                44.3389,
                44.0902,
                44.1497,
                43.6124,
                44.3278,
                44.8264,
                45.0955,
                45.4245,
                45.8433,
                46.0826,
                45.8931,
                46.0328,
                45.6140,
                46.2820,
                46.2820,
                46.0028,
                46.0328,
                46.4116,
                46.2222,
                45.6439,
                46.2122,
                46.2521,
                45.7137,
                46.4515,
                45.7835,
                45.3548,
                44.0288,
                44.1783,
                44.2181,
                44.5672,
                43.4205,
                42.6628,
                43.1314
            };
            return list;
        }

        private static IEnumerable<double> RsiResults()
        {
            var list = new List<double>
            {
                70.53,
                66.32,
                66.55,
                69.41,
                66.36,
                57.97,
                62.93,
                63.26,
                56.06,
                62.38,
                54.71,
                50.42,
                39.99,
                41.46,
                41.87,
                45.46,
                37.30,
                33.08,
                37.77
            };
            return list;
        }

        private static void Compare(IReadOnlyList<double> rsiResults, IReadOnlyList<double> results)
        {
            Assert.AreEqual(rsiResults.Count, results.Count);
            for (var i = 0; i < rsiResults.Count(); i++) Assert.AreEqual(rsiResults[i], Math.Round(results[i],2));
        }

        [TestMethod]
        public void Rsi_Test_14()
        {
            double[] close_prices = BuildCandleSticks().ToArray();
            double[] options = new double[] { 14 };

            //Find output size and allocate output space.
            int output_length = close_prices.Length - Indicators.rsi.Start(options);
            double[] output = new double[output_length];

            double[][] inputs = { close_prices };
            double[][] outputs = { output };
            int success = Indicators.rsi.Run(inputs, options, outputs);
            var value = outputs[0].ToList();

            Compare((IReadOnlyList<double>)RsiResults().ToList(), (IReadOnlyList<double>)value);
        }
    }
}
