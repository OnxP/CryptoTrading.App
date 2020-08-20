using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tulip;

namespace Ankur.Trading.Test.IndicatorsTests
{
    [TestClass]
    public class EmaTest
    {        
        private List<double> BuildCandleSticks()
        {
            var list =  new List<double>
            {
                11,
                12,
                13,
                14,
                15,
                16,
                17,
                18,
                19,
                20,
                21
            };
            return list;
        }

        private string pair = "";
        [TestMethod]
        public void Ema_Test_5()
        {
            double[] close_prices = BuildCandleSticks().ToArray();
            double[] options = new double[] { 5 };

            //Find output size and allocate output space.
            int output_length = close_prices.Length - Indicators.ema.Start(options);
            double[] output = new double[output_length];

            double[][] inputs = { close_prices };
            double[][] outputs = { output };
            int success = Indicators.ema.Run(inputs, options, outputs);
            var value = outputs[0][output.Length-1];
            Assert.AreEqual(19, Math.Round(value,0));
        }

        [TestMethod]
        public void Ema_Test_3()
        {
            double[] close_prices = new double[] { 5, 4, 5, 4, 4, 6, 5, 4, 5, 2, 5, 5, 5, 4, 4, 3 };
            double[] options = new double[] { 3 };

            //Find output size and allocate output space.
            int output_length = close_prices.Length - Indicators.ema.Start(options);
            double[] output = new double[output_length];

            double[][] inputs = { close_prices };
            double[][] outputs = { output };
            int success = Indicators.ema.Run(inputs, options, outputs);
            
            Assert.AreEqual(20, outputs[0]);
        }

        [TestMethod]
        public void Ema_Test_7()
        {
            double[] close_prices = new double[] { 5, 4, 5, 4, 4, 6, 5, 4, 5, 2, 5, 5, 5, 4, 4, 3 };
            double[] options = new double[] { 7 };

            //Find output size and allocate output space.
            int output_length = close_prices.Length - Indicators.ema.Start(options);
            double[] output = new double[output_length];

            double[][] inputs = { close_prices };
            double[][] outputs = { output };
            int success = Indicators.ema.Run(inputs, options, outputs);
            
            Assert.AreEqual(18, outputs[0]);
        }

        [TestMethod]
        public void Ema_Test_9()
        {
            double[] close_prices = new double[] { 5, 4, 5, 4, 4, 6, 5, 4, 5, 2, 5, 5, 5, 4, 4, 3 };
            double[] options = new double[] { 9 };

            //Find output size and allocate output space.
            int output_length = close_prices.Length - Indicators.ema.Start(options);
            double[] output = new double[output_length];

            double[][] inputs = { close_prices };
            double[][] outputs = { output };
            int success = Indicators.ema.Run(inputs, options, outputs);
          
            Assert.AreEqual(17, outputs[0]);
        }


    }
}
