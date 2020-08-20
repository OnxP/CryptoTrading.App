//using System.Collections.Generic;
//using Microsoft.VisualStudio.TestTools.UnitTesting;
//a
//namespace Ankur.Trading.Test.IndicatorsTests
//{
//    [TestClass]
//    public class SmaTest
//    {
//        private List<Candlestick> BuildCandleSticks()
//        {
//            var list =  new List<Candlestick>
//            {
//                new Candlestick() {Close = 11},
//                new Candlestick() {Close = 12},
//                new Candlestick() {Close = 13},
//                new Candlestick() {Close = 14},
//                new Candlestick() {Close = 15},
//                new Candlestick() {Close = 16},
//                new Candlestick() {Close = 17},
//                new Candlestick() {Close = 18},
//                new Candlestick() {Close = 19},
//                new Candlestick() {Close = 20},
//                new Candlestick() {Close = 21}
//            };
//            list.Reverse();
//            return list;
//        }

//        private string pair = "";
//        [TestMethod]
//        public void Sma_Test_5()
//        {
//            Sma sma = new Sma(BuildCandleSticks(),5, pair);
//            Assert.AreEqual(19,sma.Value);
//        }

//        [TestMethod]
//        public void Sma_Test_3()
//        {
//            Sma sma = new Sma(BuildCandleSticks(), 3, pair);
//            Assert.AreEqual(20, sma.Value);
//        }

//        [TestMethod]
//        public void Sma_Test_7()
//        {
//            Sma sma = new Sma(BuildCandleSticks(), 7, pair);
//            Assert.AreEqual(18, sma.Value);
//        }

//        [TestMethod]
//        public void Sma_Test_9()
//        {
//            Sma sma = new Sma(BuildCandleSticks(), 9, pair);
//            Assert.AreEqual(17, sma.Value);
//        }


//    }
//}
