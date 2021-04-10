using Binance;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public class MacdTradingStrategy : TradingStrategy
    {
        public MacdTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
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
            var ema200 = (Tulip.Indicators.wma, new double[] { 100 });
            var ema100 = (Tulip.Indicators.wma, new double[] { 50 });
            var psar = (Tulip.Indicators.psar, new double[] { 0.02, 0.2 });
            var srsi = (Tulip.Indicators.stochrsi2, new double[] { 14,14,3,3 });
            var adx = (Tulip.Indicators.adx, new double[] { 14 });
            dict.Add("MACD", macd);
            dict.Add("LongWma", ema200);
            dict.Add("ShortWma", ema100);
            dict.Add("PSAR", psar);
            dict.Add("SRSI", srsi);
            dict.Add("ADX", adx);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice)
        {
            var macd = indicatorOutputs["MACD"][0].ToList();
            var signal = indicatorOutputs["MACD"][1].ToList();
            var hist = indicatorOutputs["MACD"][2].ToList();
            var shortWma = indicatorOutputs["ShortWma"][0].ToList();
            var longWma = indicatorOutputs["LongWma"][0].ToList();
            var pSar = indicatorOutputs["PSAR"][0].ToList();
            var kLine = indicatorOutputs["SRSI"][0].ToList();
            var dLine = indicatorOutputs["SRSI"][1].ToList();
            var adx = indicatorOutputs["ADX"][0].ToList();

            var condition1 = macd.Last() >= signal.Last();
            var condition2 = pSar.Last() <= (double)closePrice.Close;
            var condition3 = shortWma.Last() >= longWma.Last();
            var condition4 = kLine.Last() >= dLine.Last();
            var condition5 = adx.Last()<25 && kLine.Last() >=80 && dLine.Last() >=80;
            //var condition2 = longEma.First() < (double)closePrice.Close; //this checks for an up trend however doesn't check for sideways trend.
            //var condition3 = longEma.First() > longEma.Skip(1).First(); //this checks for an up trend however doesn't check for sideways trend.
            //var condition4 = macd.First() > macd.Skip(1).First();
            //var condition5 = sRsi.Last() >= dLine.Last();
            //var condition6 = rsi.Last() <= 60;
            //var condition7 = sRsi.Last() <= 60;
            //Price > than Long EMA
            //Long EMA is in an uptrend
            //Fast > Slow EMA
            if (condition1 && condition2 && condition3 && (condition4 || condition5))
            {
                LogResult(1);
                return 1;
            }
            //check if long is trading sideways, need more entries to determin that!

            //check if long is in an uptrend.
            LogResult(0);
            return 0;
        }

        
    }
}
