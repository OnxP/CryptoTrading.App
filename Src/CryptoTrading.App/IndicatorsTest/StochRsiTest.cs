//using System;
//using System.Collections.Generic;
//using System.Linq;
//using Microsoft.VisualStudio.TestTools.UnitTesting;

//namespace Ankur.Trading.Test.IndicatorsTests
//{
//    [TestClass]
//    public class StochRsiTest
//    {
//        private static IEnumerable<Candlestick> BuildCandleSticks()
//        {
//            var list = new List<Candlestick>
//            {
//                54.0907m},
//                59.8981m},
//                58.1992m},
//                59.7562m},
//                52.3508m},
//                52.8207m},
//                56.9367m},
//                57.4695m},
//                55.2607m},
//                57.5080m},
//                54.8013m},
//                51.4717m},
//                56.1598m},
//                58.3369m},
//                56.0218m},
//                60.2219m},
//                56.7477m},
//                57.3832m},
//                50.2306m},
//                57.0617m},
//                61.5069m},
//                63.6927m},
//                66.2177m},
//                69.1576m},
//                70.7253m},
//                67.7876m},
//                68.8154m},
//                62.3843m},
//                67.5881m},
//                67.5881m},
//            };
//            return list;
//        }

//        private static IEnumerable<decimal> ShochRsiResults()
//        {
//            var list = new List<decimal>
//            {
//                0.81m,
//                0.54m,
//                1.00m,
//                0.60m,
//                0.68m,
//                0.00m,
//                0.68m,
//                1.00m,
//                1.00m,
//                1.00m,
//                1.00m,
//                1.00m,
//                0.86m,
//                0.91m,
//                0.59m,
//                0.85m,
//                0.85m
//            };
//            return list;
//        }

//        private static void Compare(IReadOnlyList<decimal> shochRsiResults, IReadOnlyList<decimal> results)
//        {
//            Assert.AreEqual(shochRsiResults.Count, results.Count);
//            for (var i = 0; i < shochRsiResults.Count(); i++) Assert.AreEqual(shochRsiResults[i], results[i]);
//        }

//        [TestMethod]
//        public void ShochRsi_Test_14()
//        {
//            var shochRsi = new StochRsi(BuildCandleSticks(),14, 14,3,3, "",BuildCandleSticks());
//            Assert.AreEqual(78.49m, Math.Round(shochRsi.KValue, 2));
//            var results = shochRsi.stochRsi.Select(value => Math.Round(value, 2)).ToList();
//            Compare(ShochRsiResults().ToList(), results);
//        }
//    }
//}