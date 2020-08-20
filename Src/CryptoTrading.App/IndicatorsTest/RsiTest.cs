//using System;
//using System.Collections.Generic;
//using System.Linq;
//using Microsoft.VisualStudio.TestTools.UnitTesting;

//namespace Ankur.Trading.Test.IndicatorsTests
//{
//    [TestClass]
//    public class RsiTest
//    {
//        private static IEnumerable<Candlestick> BuildCandleSticks()
//        {
//            var list = new List<Candlestick>
//            {
//                44.3389m},
//                44.0902m},
//                44.1497m},
//                43.6124m},
//                44.3278m},
//                44.8264m},
//                45.0955m},
//                45.4245m},
//                45.8433m},
//                46.0826m},
//                45.8931m},
//                46.0328m},
//                45.6140m},
//                46.2820m},
//                46.2820m},
//                46.0028m},
//                46.0328m},
//                46.4116m},
//                46.2222m},
//                45.6439m},
//                46.2122m},
//                46.2521m},
//                45.7137m},
//                46.4515m},
//                45.7835m},
//                45.3548m},
//                44.0288m},
//                44.1783m},
//                44.2181m},
//                44.5672m},
//                43.4205m},
//                42.6628m},
//                43.1314m}
//            };
//            list.Reverse();
//            return list;
//        }

//        private static IEnumerable<decimal> RsiResults()
//        {
//            var list = new List<decimal>
//            {
//                70.53m,
//                66.32m,
//                66.55m,
//                69.41m,
//                66.36m,
//                57.97m,
//                62.93m,
//                63.26m,
//                56.06m,
//                62.38m,
//                54.71m,
//                50.42m,
//                39.99m,
//                41.46m,
//                41.87m,
//                45.46m,
//                37.30m,
//                33.08m,
//                37.77m
//            };
//            list.Reverse();
//            return list;
//        }

//        private static void Compare(IReadOnlyList<decimal> rsiResults, IReadOnlyList<decimal> results)
//        {
//            Assert.AreEqual(rsiResults.Count, results.Count);
//            for (var i = 0; i < rsiResults.Count(); i++) Assert.AreEqual(rsiResults[i], results[i]);
//        }

//        [TestMethod]
//        public void Rsi_Test_14()
//        {
//            var rsi = new Rsi(BuildCandleSticks(), 14, "");
//            Assert.AreEqual(37.77m, Math.Round(rsi.Value, 2));
//            var results = rsi.rsi.Select(value => Math.Round(value, 2)).ToList();
//            Compare(RsiResults().ToList(), results);
//        }
//    }
//}




















































