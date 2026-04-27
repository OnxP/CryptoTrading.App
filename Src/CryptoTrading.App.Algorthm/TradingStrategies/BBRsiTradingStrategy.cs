using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class BBRsiTradingStrategy : TradingStrategy
    {
        public BBRsiTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        public BBRsiTradingStrategy(ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
        {
            noOfTrades = NoOfTrades;
        }

        protected override double StrategyWeight => 1.0 / noOfTrades;
        private double noOfTrades = 1d;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            var bbands = new IndicatorSetUp(Tulip.Indicators.bbands, new double[] { 20,2 });
            var ema = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 50 });
            var srsi = new IndicatorSetUp(Tulip.Indicators.stochrsi2, new double[] { 14 , 14 ,3 ,3 });
            var rsi = new IndicatorSetUp(Tulip.Indicators.rsi, new double[] { 14 });
            dict.Add("close", new IndicatorSetUp(Tulip.Indicators.close, new double[] { 6 }));

            dict.Add("sRsi", srsi);
            dict.Add("Rsi", rsi);
            dict.Add("BBands", bbands);
            dict.Add("LongEma", ema);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var lower = indicatorOutputs["BBands"][0].ToList();
            var middle = indicatorOutputs["BBands"][1].ToList();
            var upper = indicatorOutputs["BBands"][2].ToList();
            var longEma = indicatorOutputs["LongEma"][0].ToList();
            var rsi = indicatorOutputs["Rsi"][0].ToList();
            var kLine = indicatorOutputs["sRsi"][0].ToList();
            var dLine = indicatorOutputs["sRsi"][1].ToList();
            var close = indicatorOutputs["close"][0];


            longEma.Reverse();

            Log($"BBands - Lower: {lower.Last()}");
            Log($"BBands - Middle: {middle.Last()}");
            Log($"BBands - Upper: {upper.Last()}");
            Log($"Long Ema: {longEma.First()}");
            Log($"Rsi: {rsi.Last()}");
            Log($"SRSI - K: {kLine.Last()}");
            Log($"SRSI - D: {dLine.Last()}");
            Log($"Close Price: {closePrice}");

            // log values
            
            var condition1 = (double)closePrice.Low <= lower.Last();
            var condition2 = rsi.Last() <= 40;
            var condition3 = kLine.Last() >= dLine.Last();
            var condition4 = kLine.Last() <= 30;
            var condition5 = LastSixClose(close);

            //Price > than Long EMA
            //Long EMA is in an uptrend
            //Fast > Slow EMA
            return StopLimitTrackers.SetStopLimit(indicatorOutputs, closePrice,
                condition1 && condition2 && condition3 && condition4 && condition5,
                s => Logger.LogInformation(s));
        }
        private bool LastSixClose(double[] close)
        {
            if (close[1] < close[2] && close[2] < close[3] && close[3] < close[4] && close[4] < close[5] && close[5] < close[6])
                return true;
            else if (close[1] < close[2] && close[2] < close[3] && close[3] < close[4])
                return true;
            else if (close[2] < close[3] && close[3] < close[4] && close[4] < close[5] && close[5] < close[6])
                return true;
            else if (close[1] < close[2] && close[3] < close[4] && close[4] < close[5] && close[5] < close[6])
                return true;
            else if (close[1] < close[2] && close[2] < close[3] && close[4] < close[5] && close[5] < close[6])
                return true;
            else if (close[1] < close[2] && close[2] < close[3] && close[3] < close[4] && close[5] < close[6])
                return true;
            else if (close[1] < close[2] && close[2] < close[3] && close[3] < close[4] && close[4] < close[5])
                return true;
            else return false;
        }

    }
}
