using Binance;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public class WmaBounce : TradingStrategy
    {
        public WmaBounce(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        protected override double StrategyWeight => 1.0;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, (Indicator indicator, double[] options)> GenerateIndicators()
        {
            var dict = new Dictionary<string, (Indicator indicator, double[] options)>();

            //add indicators to dictionary
            double shortPeriod = 12;
            double longPeriod = 26;
            double signal = 9;
            var macd = (Tulip.Indicators.macd, new double[] { shortPeriod, longPeriod,signal });
            var ema100 = (Tulip.Indicators.wma, new double[] { 100 });
            var ema50 = (Tulip.Indicators.wma, new double[] { 50 });
            var ema25 = (Tulip.Indicators.wma, new double[] { 25 });
            var psar = (Tulip.Indicators.psar, new double[] { 0.02, 0.2 });
            var srsi = (Tulip.Indicators.stochrsi2, new double[] { 14,14,3,3 });
            var adx = (Tulip.Indicators.adx, new double[] { 14 });
            var dc = (Tulip.Indicators.adx, new double[] { 20 });
            var bbands = (Tulip.Indicators.bbands, new double[] { 20,2 });
            dict.Add("MACD", macd);
            dict.Add("LongWma", ema100);
            dict.Add("MediumWma", ema50);
            dict.Add("ShortWma", ema25);
            dict.Add("PSAR", psar);
            dict.Add("SRSI", srsi);
            dict.Add("ADX", adx);
            dict.Add("DC", dc);
            dict.Add("BB", bbands);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice)
        {
            var macd = indicatorOutputs["MACD"][0].ToList();
            var signal = indicatorOutputs["MACD"][1].ToList();
            var hist = indicatorOutputs["MACD"][2].ToList();
            var shortWma = indicatorOutputs["ShortWma"][0].ToList();
            var longWma = indicatorOutputs["LongWma"][0].ToList();
            var mediumWma = indicatorOutputs["MediumWma"][0].ToList();
            var pSar = indicatorOutputs["PSAR"][0].ToList();
            var kLine = indicatorOutputs["SRSI"][0].ToList();
            var dLine = indicatorOutputs["SRSI"][1].ToList();
            var adx = indicatorOutputs["ADX"][0].ToList();
            var dcUpper = indicatorOutputs["DC"][2].ToList();
            var bbLower = indicatorOutputs["BB"][0].ToList();
            var bbMedium = indicatorOutputs["BB"][1].ToList();
            var bbUpper = indicatorOutputs["BB"][2].ToList();

            dcUpper.Reverse();

            var condition1 = macd.Last() >= signal.Last();
            var condition2 = pSar.Last() <= (double)closePrice.Close;
            var condition3 = mediumWma.Last() >= longWma.Last();
            var condition4 = shortWma.Last() >= mediumWma.Last();
            var condition5 = kLine.Last() >= dLine.Last();
            var condition6 = adx.Last()<25 && kLine.Last() >=80.0 && dLine.Last() >=80.0;
            var macdDivergance = (((macd.Last() - signal.Last()) / signal.Last()) * 100.0);
            var condition7 = macdDivergance > 20.0 || macdDivergance < -20.0;
            var condition8 = (double)closePrice.High >= dcUpper.Skip(1).First();
            var condition9 = dcUpper.Skip(1).First() == dcUpper.Skip(2).First() && dcUpper.Skip(2).First() == dcUpper.Skip(3).First();

            shortWma.Reverse();
            mediumWma.Reverse();
            longWma.Reverse();

            var condition10 = EmaBounce(shortWma, closePrice) && EmaBounce(mediumWma, closePrice) && EmaBounce(longWma,closePrice) && closePrice.Open < closePrice.Close;

            //Price > than Long EMA
            //Long EMA is in an uptrend
            //Fast > Slow EMA
             if (condition10)
            {
                LogResult(1);
                return 1;
            }
            //check if long is trading sideways, need more entries to determin that!

            //check if long is in an uptrend.
            LogResult(0);
            return 0;
        }

        private bool EmaBounce(List<double> wma, Candlestick closePrice)
        {
            if (lastClose == null)
            {
                lastClose = closePrice;
                return false;
            }
            if (wma.Count() == 1)
            {
                return false;
            }
            var close = (double)closePrice.Close > wma.First();
            var bounce = (double)closePrice.Low < wma.First() && (double)closePrice.Close > wma.First();
            var previousPassThrough = (double)lastClose.Close < wma.Skip(1).First() && (double)lastClose.High > wma.Skip(1).First();
            lastClose = closePrice;
            return bounce && !previousPassThrough && close;
        }
        public Candlestick lastClose;
    }
}
