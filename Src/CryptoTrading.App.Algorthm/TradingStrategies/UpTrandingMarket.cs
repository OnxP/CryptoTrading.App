using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class UptrendingMarket : TradingStrategy
    {
        public UptrendingMarket(ILogger<TradingStrategy> logger, ISymbolCache symbolCache) : base(logger, symbolCache)
        {
        }

        protected override double StrategyWeight => 1.0;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            //add indicators to dictionary
            double shortPeriod = 12;
            double longPeriod = 26;
            double signal = 9;
            var macd = new IndicatorSetUp(Tulip.Indicators.macd, new double[] { shortPeriod, longPeriod,signal });
            var ema200 = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 100 });
            var ema100 = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 25 });
            var ema50 = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 12 });
            var vwap = new IndicatorSetUp(Tulip.Indicators.vwap, new double[] { 0 });
            var rsi = new IndicatorSetUp(Tulip.Indicators.rsi, new double[] { 14 });
            var adx = new IndicatorSetUp(Tulip.Indicators.adx, new double[] { 14, 14 });
            var srsi = new IndicatorSetUp(Tulip.Indicators.stochrsi, new double[] { 14 });


            dict.Add("MACD", macd);
            dict.Add("LongEma", ema200);
            dict.Add("MediumEma", ema100);
            dict.Add("ShortEma", ema50);
            dict.Add("VWAP", vwap);
            dict.Add("RSI", rsi);
            dict.Add("ADX", adx);
            dict.Add("SRSI", srsi);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice,
            IStopLimitTracker StopLimitTrackers)
        {
            var macd = indicatorOutputs["MACD"][0].ToList();
            var signal = indicatorOutputs["MACD"][1].ToList();
            var hist = indicatorOutputs["MACD"][2].ToList();
            var longEma = indicatorOutputs["LongEma"][0].ToList();
            var mediumEma = indicatorOutputs["MediumEma"][0].ToList();
            var shortEma = indicatorOutputs["ShortEma"][0].ToList();
            var rsi = indicatorOutputs["RSI"][0].ToList();
            var adx = indicatorOutputs["ADX"][0].ToList();
            var srsi = indicatorOutputs["SRSI"][0].ToList();
            var vWap = indicatorOutputs["VWAP"][0].ToList();

            var kLine = new List<double>();

            for (int i = 0; i <= srsi.Count() - 3; i++)
                kLine.Add(srsi.Skip(i).Take(3).Average());

            var dLine = new List<double>();

            for (int i = 0; i <= kLine.Count() - 3; i++)
                dLine.Add(kLine.Skip(i).Take(3).Average());

            Log($"MACD - Line: {macd.First()}");
            Log($"MACD - Signal Line: {signal.First()}");
            Log($"Long Ema: {longEma.First()}");
            Log($"Close Price: {closePrice}");

            //Up Trend Conditions
            var upTrendCondition = mediumEma.Last() < shortEma.Last() && adx.Last() > 30;


            var condition1 = macd.Last() >= signal.Last();
            var condition3 = rsi.Last() < 70;
            var condition2 = shortEma.Last() < (double)closePrice.Close;
            //var condition4 = kLine.Last()< 0.8;
            if (condition1)
            {
                SetStopLimit(indicatorOutputs, closePrice, StopLimitTrackers);
                return 1;
            }

            return 0;
        }

    }
}
