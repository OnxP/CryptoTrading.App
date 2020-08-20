using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ankur.Trading.Test.IndicatorsTests
{
    [TestClass]
    public class MaComparisioncs
    {
        private static IEnumerable<double> BuildCandleSticks()
        {
            var list = new List<double>
            {
                22.2734,
                22.1940,
                22.0847,
                22.1741,
                22.1840,
                22.1344,
                22.2337,
                22.4323,
                22.2436,
                22.2933,
                22.1542,
                22.3926,
                22.3816,
                22.6109,
                23.3558,
                24.0519,
                23.7530,
                23.8324,
                23.9516,
                23.6338,
                23.8225,
                23.8722,
                23.6537,
                23.1870,
                23.0976,
                23.3260,
                22.6805,
                23.0976,
                22.4025,
                22.1725
            };
            list.Reverse();
            return list;
        }
        
        private static IEnumerable<decimal> SmaResults()
        {
            var list = new List<decimal>
            {
                22.225m,
                22.213m,
                22.233m,
                22.262m,
                22.306m,
                22.423m,
                22.615m,
                22.767m,
                22.907m,
                23.078m,
                23.212m,
                23.379m,
                23.527m,
                23.654m,
                23.711m,
                23.686m,
                23.613m,
                23.506m,
                23.432m,
                23.277m,
                23.131m
            };
            list.Reverse();
            return list;
        }

        private static IEnumerable<decimal> EmaResults()
        {
            var list = new List<decimal>
            {
                22.225m,
                22.212m,
                22.245m,
                22.270m,
                22.332m,
                22.518m,
                22.797m,
                22.971m,
                23.127m,
                23.277m,
                23.342m,
                23.429m,
                23.510m,
                23.536m,
                23.473m,
                23.404m,
                23.390m,
                23.261m,
                23.231m,
                23.081m,
                22.916m
            };
            list.Reverse();
            return list;
        }

        //private string pair = "";
        //[TestMethod]
        //public void Ema_Test_10()
        //{
        //    var ema = new Ema(BuildCandleSticks(), 10, pair);
        //    Assert.AreEqual(22.916m, Math.Round(ema.Value, 3));
        //    var results = ema.ema.Select(value => Math.Round(value, 3)).ToList();
        //    Compare(EmaResults().ToList(), results);
        //}

        //[TestMethod]
        //public void Sma_Test_10()
        //{
        //    var sma = new Sma(BuildCandleSticks(), 10, pair);
        //    Assert.AreEqual(23.131m, Math.Round(sma.Value, 3));
        //    var results = sma.sma.Select(value => Math.Round(value, 3)).ToList();
        //    Compare(SmaResults().ToList(), results);
        //}

        //private static void Compare(IReadOnlyList<decimal> smaResults, IReadOnlyList<decimal> results)
        //{
        //    Assert.AreEqual(smaResults.Count, results.Count);
        //    for (var i = 0; i < smaResults.Count(); i++) Assert.AreEqual(smaResults[i], results[i]);
        //}
    }
}