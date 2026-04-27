using System;
using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class TrendMeterTradingStrategy : TradingStrategy
    {
        public TrendMeterTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }
        public TrendMeterTradingStrategy(ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
        {
            noOfTrades = NoOfTrades;
        }
        protected override double StrategyWeight => 1.0 / noOfTrades;
        private double noOfTrades = 1d;
        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();
            dict.Add("MACD",new IndicatorSetUp(Tulip.Indicators.macd,new double[]{8,12,5}));
            dict.Add("RSI13",new IndicatorSetUp(Tulip.Indicators.rsi,new double[]{13}));
            dict.Add("RSI5",new IndicatorSetUp(Tulip.Indicators.rsi,new double[]{5}));
            dict.Add("EMA5",new IndicatorSetUp(Tulip.Indicators.ema,new double[]{5}));
            dict.Add("EMA11",new IndicatorSetUp(Tulip.Indicators.ema,new double[]{11}));
            dict.Add("EMA13",new IndicatorSetUp(Tulip.Indicators.ema,new double[]{13}));
            dict.Add("EMA50",new IndicatorSetUp(Tulip.Indicators.ema,new double[]{50}));
            dict.Add("EMA200",new IndicatorSetUp(Tulip.Indicators.ema,new double[]{200}));
            dict.Add("SMA36",new IndicatorSetUp(Tulip.Indicators.sma,new double[]{36}));
            dict.Add("close", new IndicatorSetUp(Tulip.Indicators.close, new double[] { 100 }));
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {

            ///change to hull suit and boom pro
            /// https://www.youtube.com/watch?v=wtB4fHM-IxM
            var macd = indicatorOutputs["MACD"][0].ToList();
            var signal = indicatorOutputs["MACD"][1].ToList();
            var hist = indicatorOutputs["MACD"][2].ToList();
            var ema5 = indicatorOutputs["EMA5"][0].ToList();
            var ema11 = indicatorOutputs["EMA11"][0].ToList();
            var ema13 = indicatorOutputs["EMA13"][0].ToList();
            var ema50 = indicatorOutputs["EMA50"][0].ToList();
            var ema200 = indicatorOutputs["EMA200"][0].ToList();
            var sma36 = indicatorOutputs["SMA36"][0].ToList();
            var rsi5 = indicatorOutputs["RSI5"][0].ToList();
            var rsi13 = indicatorOutputs["RSI13"][0].ToList();
            var close = indicatorOutputs["close"][0].ToList();
            var open = indicatorOutputs["close"][4].ToList();

            macd.Reverse();
            signal.Reverse();
            rsi13.Reverse();
            rsi5.Reverse();
            ema11.Reverse();
            ema13.Reverse();
            ema200.Reverse();
            ema5.Reverse();
            ema50.Reverse();
            sma36.Reverse();

            var condition1 = macd.First() - signal.First() > 0; //Trend Meter 1
            var condition2 = rsi5.First() > 50 && rsi13.First() > 50; //Trend Meter 2 and 3

            var condition1a = macd.Skip(1).First() - signal.Skip(1).First() > 0;
            var condition2a = rsi5.Skip(1).First() < 50 || rsi13.Skip(1).First() < 50;

            var previousInvlidSignal = condition1a || condition2a;

            var condition3 = ema11.First() < ema5.First(); // Trend bar 1
            var condition4 = sma36.First() < ema13.First();// Trend bar 2
            var condition5 = ema200.First() < ema50.First();
            var condition6 = (closePrice.Close - closePrice.Open) > (decimal)StandardDeviation(close,open);

            var spike = closePrice.Close - closePrice.Open;
            var upperLine = StandardDeviation(close, open);
            var lowerLine = upperLine * -1;

            //take trade, SL at swing Low set target 10X price.
            return StopLimitTrackers.SetSwingLowStopLimit(indicatorOutputs, closePrice, condition1 && condition3 &&condition4 && condition5 && condition6 && previousInvlidSignal&& condition2 && upperLine>=0.00001,!condition1,Last10Low,
                s => Logger.LogInformation(s));
            //exit trade when condition 1 and 2 are not true.

        }

        public double StandardDeviation(List<double> close, List<double> open)
        {
            double standardDeviation = 0;

            var values = new List<double>();

            for (int i = close.Count() - 1; i >= 0; i--)
            {
                if (close[i] == 0) continue;
                values.Add(close[i] - open[i]);
            }

            if (!values.Any()) return standardDeviation;

            var avg = sma(values, values.Count);
            double sumOfSquareDeviations = values.Select(t => Sum(t, -avg)).Select(sum => (sum * sum)).Sum();

            standardDeviation = Math.Sqrt((sumOfSquareDeviations) / (values.Count()));

            return standardDeviation;
        }

        private double Sum(double v, double avg)
        {
            return v + avg;
        }

        private double sma(List<double> input, int period)
        {
            double sum = default;
            for (var i = 0; i < period; ++i)
            {
                sum += input[i];
            }

            double scale = 1.0 / period;
            var lastsma = sum * scale;
            for (var i = period; i < input.Count; ++i)
            {
                sum = sum + input[i] - input[i - period];
                lastsma = sum * scale;
            }

            return lastsma;
        }
    }
}
