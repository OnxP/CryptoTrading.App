using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace Ankur.Trading.Test.IndicatorsTests
{
    [TestClass]
    public class MacdTest
    {
        private static IEnumerable<double> BuildCandleSticks()
        {
            var list = new List<double>
            {
                459.99
                ,448.85
                ,446.06
                ,450.81
                ,442.8
                ,448.97
                ,444.57
                ,441.4
                ,430.47
                ,420.05
                ,431.14
                ,425.66
                ,430.58
                ,431.72
                ,437.87
                ,428.43
                ,428.35
                ,432.5
                ,443.66
                ,455.72
                ,454.49
                ,452.08
                ,452.73
                ,461.91
                ,463.58
                ,461.14
                ,452.08
                ,442.66
                ,428.91
                ,429.79
                ,431.99
                ,427.72
                ,423.2
                ,426.21
                ,426.98
                ,435.69
                ,434.33
                ,429.8
                ,419.85
                ,426.24
                ,402.8
                ,392.05
                ,390.53
                ,398.67
                ,406.13
                ,405.46
                ,408.38
                ,417.2
                ,430.12
                ,442.78
                ,439.29
                ,445.52
                ,449.98
                ,460.71
                ,458.66
                ,463.84
                ,456.77
                ,452.97
                ,454.74
                ,443.86
                ,428.85
                ,434.58
                ,433.26
                ,442.93
                ,439.66
                ,441.35
            };
            return list;
        }

        private static IEnumerable<double> MacdLineResult()
        {
            var list = new List<double>
            {
                3.10203507
                ,2.930305258
                ,2.010912325
                ,0.170807751
                ,-1.20261672
                ,-2.089457923
                ,-3.101091824
                ,-4.21891221
                ,-4.806504681
                ,-5.150669669
                ,-4.666802698
                ,-4.343011735
                ,-4.401203867
                ,-5.190372018
                ,-5.239773024
                ,-7.088622553
                ,-9.313919221
                ,-11.0724973
                ,-11.67477361
                ,-11.41849694
                ,-11.14103235
                ,-10.56374776
                ,-9.287484925
                ,-7.151068349
                ,-4.385829511
                ,-2.447755442
                ,-0.404446636
                ,1.556828886
                ,3.931653538
                ,5.583933211
                ,7.228039129
                ,7.869796881
                ,7.979780341
                ,8.116208631
                ,7.262684259
                ,5.313824033
                ,4.183476858
                ,3.144903125
                ,3.066762415
                ,2.7097375
                ,2.533951741
            };
            return list;
        }

        private static IEnumerable<double> SignleLineResult()
        {
            var list = new List<double>
            {
                3.10203507
                ,3.067689107
                ,2.856333751
                ,2.319228551
                ,1.614859497
                ,0.873996013
                ,0.078978445
                ,-0.780599686
                ,-1.585780685
                ,-2.298758482
                ,-2.772367325
                ,-3.086496207
                ,-3.349437739
                ,-3.717624595
                ,-4.022054281
                ,-4.635367935
                ,-5.571078192
                ,-6.671362014
                ,-7.672044333
                ,-8.421334854
                ,-8.965274353
                ,-9.284969034
                ,-9.285472212
                ,-8.85859144
                ,-7.964039054
                ,-6.860782331
                ,-5.569515192
                ,-4.144246377
                ,-2.529066394
                ,-0.906466473
                ,0.720434648
                ,2.150307094
                ,3.316201744
                ,4.276203121
                ,4.873499349
                ,4.961564286
                ,4.8059468
                ,4.473738065
                ,4.192342935
                ,3.895821848
                ,3.623447827

            };
            return list;
        }

        private static void Compare(IReadOnlyList<double> rsiResults, IReadOnlyList<double> results)
        {
            Assert.AreEqual(rsiResults.Count, results.Count);
            for (var i = 0; i < rsiResults.Count(); i++) Assert.AreEqual(Math.Round(rsiResults[i],4), Math.Round(results[i], 4));
        }

        [TestMethod]
        public void Macd_Test()
        {
            double[] close_prices = BuildCandleSticks().ToArray();
            double[] options = new double[] { 12,26, 9};

            //Find output size and allocate output space.
            int output_length = close_prices.Length - Indicators.macd.Start(options);
            double[] output = new double[output_length];
            double[] output1 = new double[output_length];
            double[] output2 = new double[output_length];

            double[][] inputs = { close_prices };
            double[][] outputs = { output, output1, output2 };
            int success = Indicators.macd.Run(inputs, options, outputs);
            var value = outputs[0].ToList();
            var value1 = outputs[1].ToList();

            Compare((IReadOnlyList<double>)MacdLineResult().ToList(), (IReadOnlyList<double>)value);
            Compare((IReadOnlyList<double>)SignleLineResult().ToList(), (IReadOnlyList<double>)value1);
        }
    }
}
