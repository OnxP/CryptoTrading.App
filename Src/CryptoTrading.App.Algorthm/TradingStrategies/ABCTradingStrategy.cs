using System;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class AbcTradingStrategy : TradingStrategy
    {
        public AbcTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        protected override double StrategyWeight => 1.0;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            dict.Add("maxima",new IndicatorSetUp(Tulip.Indicators.max, new double[] { 10 }));
            dict.Add("minima",new IndicatorSetUp(Tulip.Indicators.min, new double[] { 10 }));

            return dict;
        }

        public bool lastMinima { get; set; } = false;
        public double err_allowed = 10d/100d;
        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice,
            IStopLimitTracker StopLimitTrackers)
        {
            var maxima = indicatorOutputs["maxima"][0].ToList();
            var minima = indicatorOutputs["minima"][0].ToList();
            bool conditionsMet = false;
            //reverse lists

            //take last 10 candle sticks work out the min and max and select the closest one.

            lastMinima = LastMaxCalc(closePrice, ClosePrices.GetCandlesticks.Skip(1).Take(10));

            if (lastMinima)
            {
                maxima.Reverse();
                minima.Reverse();
                var countd = 1;
                var countc = 1;
                var countb = 1;
                foreach (var d in minima.Take(3))
                {
                    foreach (var c in maxima.Take(6))
                    {
                        foreach (var b in minima.Skip(countd).Take(20))
                        {
                            foreach (var a in maxima.Skip(countc).Take(20))
                            {
                                foreach (var x in minima.Skip(countd+countc).Take(40))
                                {
                                    var CD = d - c;
                                    var BC = c - b;
                                    var AB = b - a;
                                    var XA = a - x;
                                    if (CheckHarmonics(Math.Abs(XA), Math.Abs(AB), Math.Abs(BC), Math.Abs(CD)))
                                    {
                                        conditionsMet = true;
                                        break;
                                    }
                                }
                            }

                            countb++;
                        }

                        countc++;
                    }

                    countd++;
                }
            }

            return StopLimitTrackers.SetStopLimit(indicatorOutputs, closePrice, conditionsMet,
                s => Logger.LogInformation(s));

        }

        private bool CheckHarmonics(double XA, double AB, double BC, double CD)
        {
            return CheckBat(XA, AB, BC, CD) || CheckButterfly(XA, AB, BC, CD) || CheckCrab(XA, AB, BC, CD) || CheckCypher(XA, AB, BC, CD) || CheckGartley(XA, AB, BC, CD);// || CheckShark(XA, AB, BC, CD);
        }

        private bool CheckShark(double xa, double ab, double bc, double cd)
        {
            (double, double) AB_Range = ((0.618 - err_allowed) * xa, ((0.618 + err_allowed) * xa));
            (double, double) BC_Range = ((0.382 - err_allowed) * ab, ((0.886 + err_allowed) * ab));
            (double, double) CD_Range = ((1.27 - err_allowed) * bc, ((1.618 + err_allowed) * bc));

            bool con1 = AB_Range.Item1 < ab && ab < AB_Range.Item2;
            bool con2 = BC_Range.Item1 < bc && bc < BC_Range.Item2;
            bool con3 = CD_Range.Item1 < cd && cd < CD_Range.Item2;

            if (con1 && con2 && con3)
                return true;
            return false;
        }

        private bool CheckGartley(double xa, double ab, double bc, double cd)
        {
            (double, double) AB_Range = ((0.618 - err_allowed)*xa,((0.618 + err_allowed) * xa));
            (double, double) BC_Range = ((0.382 - err_allowed)*ab,((0.886 + err_allowed) * ab));
            (double, double) CD_Range = ((1.27 - err_allowed)*bc,((1.618 + err_allowed) * bc));

            bool con1 = AB_Range.Item1 < ab && ab < AB_Range.Item2;
            bool con2 = BC_Range.Item1 < bc && bc < BC_Range.Item2;
            bool con3 = CD_Range.Item1 < cd && cd < CD_Range.Item2;

            if (con1 && con2 && con3)
                return true;
            return false;
        }

        private bool CheckCypher(double xa, double ab, double bc, double cd)
        {
            (double, double) AB_Range = ((0.382 - err_allowed) * xa, ((0.618 + err_allowed) * xa));
            (double, double) BC_Range = ((1.13 - err_allowed) * ab, ((1.414 + err_allowed) * ab));
            (double, double) CD_Range = ((1.272 - err_allowed) * bc, ((1.618 + err_allowed) * bc));

            bool con1 = AB_Range.Item1 < ab && ab < AB_Range.Item2;
            bool con2 = BC_Range.Item1 < bc && bc < BC_Range.Item2;
            bool con3 = CD_Range.Item1 < cd && cd < CD_Range.Item2;

            if (con1 && con2 && con3)
                return true;
            return false;
        }

        private bool CheckCrab(double xa, double ab, double bc, double cd)
        {
            (double, double) AB_Range = ((0.382 - err_allowed) * xa, ((0.618 + err_allowed) * xa));
            (double, double) BC_Range = ((0.382 - err_allowed) * ab, ((0.886 + err_allowed) * ab));
            (double, double) CD_Range = ((2.24 - err_allowed) * bc, ((3.618 + err_allowed) * bc));

            bool con1 = AB_Range.Item1 < ab && ab < AB_Range.Item2;
            bool con2 = BC_Range.Item1 < bc && bc < BC_Range.Item2;
            bool con3 = CD_Range.Item1 < cd && cd < CD_Range.Item2;

            if (con1 && con2 && con3)
                return true;
            return false;
        }

        private bool CheckButterfly(double xa, double ab, double bc, double cd)
        {
            (double, double) AB_Range = ((0.786 - err_allowed) * xa, ((0.786 + err_allowed) * xa));
            (double, double) BC_Range = ((0.382 - err_allowed) * ab, ((0.886 + err_allowed) * ab));
            (double, double) CD_Range = ((1.618 - err_allowed) * bc, ((2.618 + err_allowed) * bc));

            bool con1 = AB_Range.Item1 < ab && ab < AB_Range.Item2;
            bool con2 = BC_Range.Item1 < bc && bc < BC_Range.Item2;
            bool con3 = CD_Range.Item1 < cd && cd < CD_Range.Item2;

            if (con1 && con2 && con3)
                return true;
            return false;
        }

        private bool CheckBat(double xa, double ab, double bc, double cd)
        {
            (double, double) AB_Range = ((0.382 - err_allowed) * xa, ((0.5 + err_allowed) * xa));
            (double, double) BC_Range = ((0.382 - err_allowed) * ab, ((0.886 + err_allowed) * ab));
            (double, double) CD_Range = ((1.618 - err_allowed) * bc, ((2.618 + err_allowed) * bc));

            bool con1 = AB_Range.Item1 < ab && ab < AB_Range.Item2;
            bool con2 = BC_Range.Item1 < bc && bc < BC_Range.Item2;
            bool con3 = CD_Range.Item1 < cd && cd < CD_Range.Item2;

            if (con1 && con2 && con3)
                return true;
            return false;
        }

        private bool LastMaxCalc(ExchangeCandlestick closePrice, IEnumerable<ExchangeCandlestick> take)
        {
            ExchangeCandlestick max = closePrice;
            ExchangeCandlestick min = closePrice;
            foreach (var stick in take)
            {
                if (max.High > stick.High) max = stick;
                if (min.Low > stick.Low) min = stick;
            }

            if (max.CloseTime < min.CloseTime) return true;
            return false;
        }
    }
}
