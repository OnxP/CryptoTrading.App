using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class MovingAverangeTradingStrategy : TradingStrategy
    {
        public MovingAverangeTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        public MovingAverangeTradingStrategy(ILogger<TradingStrategy> logger, Indicator stratgy, double NoOfTrades):this(logger)
        {
            indicator = stratgy;
            noOfTrades = NoOfTrades;
        }

        public MovingAverangeTradingStrategy(ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
        {
            indicator = Tulip.Indicators.wma;
            noOfTrades = NoOfTrades;
        }

        protected override double StrategyWeight => 1.0d/ noOfTrades;
        private double noOfTrades = 1d;

        private Indicator indicator = Tulip.Indicators.kama;
        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();
            dict.Add("25", new IndicatorSetUp(indicator, new double[] { 25 }));
            dict.Add("50", new IndicatorSetUp(indicator, new double[] { 50 }));
            dict.Add("100", new IndicatorSetUp(indicator, new double[] { 100 }));
            dict.Add("Srsi", new IndicatorSetUp(Tulip.Indicators.stochrsi2, new double[] { 14,14,3,3 }));
            dict.Add("rsi", new IndicatorSetUp(Tulip.Indicators.rsi, new double[] { 14 }));
            dict.Add("close", new IndicatorSetUp(Tulip.Indicators.close, new double[] { 6 }));
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var MA25 = indicatorOutputs["25"][0].ToList();
            var MA50 = indicatorOutputs["50"][0].ToList();
            var MA100= indicatorOutputs["100"][0].ToList();
            var close = indicatorOutputs["close"][0];
            // log values
            
            var condition1 = MA50.Last() >= MA100.Last();
            var condition3 = MA25.Last() >= MA50.Last();
            var condition2 = MA50.Last() <= (double)closePrice.Close;
            var condition5 = LastSixClose(close);
            if (condition1 && condition2 && condition5)
            {
                SetStopLimit(indicatorOutputs, closePrice, StopLimitTrackers);
                return 1;
            }
            return 0;
        }

        private bool LastSixClose(double[] close)
        {
            if (close[0] < close[1] && close[1] < close[2] && close[2] < close[3] && close[3] < close[4] && close[4] < close[5])
                return true;
            else if (close[0] < close[1] && close[1] < close[2] && close[2] < close[3])
                return true;
            else if (close[1] < close[2] && close[2] < close[3] && close[3] < close[4] && close[4] < close[5])
                return true;
            else if (close[0] < close[1] && close[2] < close[3] && close[3] < close[4] && close[4] < close[5])
                return true;
            else if (close[0] < close[1] && close[1] < close[2] && close[3] < close[4] && close[4] < close[5])
                return true;
            else if (close[0] < close[1] && close[1] < close[2] && close[2] < close[3] && close[4] < close[5])
                return true;
            else if (close[0] < close[1] && close[1] < close[2] && close[2] < close[3] && close[3] < close[4])
                return true;
            else return false;
        }
    }
}
