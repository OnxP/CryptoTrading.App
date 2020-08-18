using System;
using Indicators;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IndicatorsTest
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestMethod1()
        {
            var results = Indicators.Indicators.ema;

            Assert.IsNotNull(results);
        }
    }
}
