using System;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class MeanReversionTradingStrategy : TradingStrategy
    {
        public MeanReversionTradingStrategy(ILogger<TradingStrategy> logger, ISymbolCache symbolCache) : base(logger, symbolCache)
        {
        }

        protected override double StrategyWeight => 1.0;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp> { { "Close", new IndicatorSetUp(Tulip.Indicators.close, new double[] { 100 }) }, { "sma", new IndicatorSetUp(Tulip.Indicators.sma, new double[] { 4 }) } };
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var close = indicatorOutputs["Close"][0].ToList();
            var open = indicatorOutputs["Close"][4].ToList();
            var sma = indicatorOutputs["sma"][0].ToList();
            var std = new List<double>();
            for (int i = 0; i < open.Count-10; ++i)
            {
                std.Add(standardDeviation(open.Skip(i).Take(10)));
            }

            var prevStd = std.Average();
            sma.Reverse();
            close.Reverse();

            var condition1 = close[1] < sma[1] - prevStd;
            var condition4 = (double)closePrice.Close > sma.First();

            if (!StopLimitTrackers.IsOpen && condition4 && condition1)
            {
                //var diff = closePrice.Close - Last10Low;
                //volume and profitability conditions.
                if (closePrice.QuoteVolume > 2.0m && closePrice.NumberOfTrades > 10m && SymbolCache.Get(closePrice.Symbol).TickSize * 3 < (decimal)prevStd)
                {
                    //if(diff < 3* SymbolCache.Get(closePrice.Symbol).TickSize) return false;
                    StopLimitTrackers.CurrentPrice = closePrice.Close;
                    StopLimitTrackers.Pair = closePrice.Symbol;
                    StopLimitTrackers.TargetPrice = AdjustForMinimum(SymbolCache.Get(closePrice.Symbol).TickSize,
                        closePrice.Close + (decimal)prevStd);
                    StopLimitTrackers.StopLimitPrice = AdjustForMinimum(SymbolCache.Get(closePrice.Symbol).TickSize,
                        closePrice.Close - (decimal)prevStd);

                    LogResult(closePrice.CloseTime, closePrice.Symbol, closePrice.Close, StopLimitTrackers.TargetPrice,
                        StopLimitTrackers.StopLimitPrice);
                    return 1;

                }

                return 0;
            }


            return 0;
        }

        private double standardDeviation(IEnumerable<double> sequence)
        {
            double result = 0;

            if (sequence.Any())
            {
                double average = sequence.Average();
                double sum = sequence.Sum(d => Math.Pow(d - average, 2));
                result = Math.Sqrt((sum) / sequence.Count());
            }
            return result;
        }
    }
}
