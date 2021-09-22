using Binance;
using CryptoTrading.App.Algorthm.CustomIndicators;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public class PriceAction9TradingStrategy : TradingStrategy
    {
        public PriceAction9TradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        public PriceAction9TradingStrategy (ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
        {
            noOfTrades = NoOfTrades;
        }

        protected override double StrategyWeight => 1.0 / noOfTrades;
        private double noOfTrades = 1d;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, (Tulip.Indicator indicator, double[] options)> GenerateIndicators()
        {
            var dict = new Dictionary<string, (Tulip.Indicator indicator, double[] options)>();

            var atr = (Tulip.Indicators.atr, new double[] { 14 });
            dict.Add("close", (Tulip.Indicators.close, new double[] { 40 }));
            dict.Add("atr", atr);
            return dict;
        }

        private SupportResistance _supportResistance;
        private decimal _average;
        public override double Calculate(OrderedFixedLengthList<Candlestick> closePrices, IStopLimitTracker StopLimitTrackers)
        {
            //convert from 15min to hourly scale
            var numberToSkip = 0;
            switch(closePrices.Last().CloseTime.Minute)
            {
                case 0:
                    numberToSkip = 0;
                    break;
                case 15:
                    numberToSkip = 1;
                    break;
                case 30:
                    numberToSkip = 2;
                    break;
                case 45:
                    numberToSkip = 3;
                    break;
            }


            var candles = closePrices.ToList();
            candles.Reverse();
            _supportResistance = new SupportResistance(candles);
            _average = AverageCandleSize(candles);
            return base.Calculate(closePrices, StopLimitTrackers);
        }
        public decimal AverageCandleSize(List<Candlestick> Candles)
        {
            decimal AverageCandleSize = 0.0m;
            for (int i = 1; i < Candles.Count; ++i)
            {
                AverageCandleSize += Math.Abs(Candles[i].High - Candles[i].Low);
            }
            AverageCandleSize /= ((decimal)(Candles.Count));
            return AverageCandleSize;
        }
        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var atr = indicatorOutputs["atr"][0].ToList();
            var close = indicatorOutputs["close"][0].ToList();

            //close.Reverse();
            int uCount = 0;
            int dCount = 0;
            for (int i=0;i<=9;i++)
            {
                uCount += close[i] < close[i+4] ? 1 : 0;
                dCount += close[i] > close[i+4] ? 1 : 0;
            }

            if (StopLimitTrackers.IsOpen)
            {
                StopLimitTrackers.StopLimitPrice = _supportResistance.GetNextSupportLevel(closePrice.Close, _average * 2) - Convert.ToDecimal(atr.Last());
                StopLimitTrackers.TargetPrice = _supportResistance.GetNextResistanceLevel(closePrice.Close, _average) + Convert.ToDecimal(atr.Last());
            }

            if (uCount>=9 && _supportResistance.IsAtSupportResistance(closePrice.Close,_average))
            {
                LogResult(1);
                if (!StopLimitTrackers.IsOpen)
                {
                    StopLimitTrackers.StopLimitPrice = _supportResistance.GetNextSupportLevel(closePrice.Close, _average * 2) - Convert.ToDecimal(atr.Last());
                    StopLimitTrackers.TargetPrice = _supportResistance.GetNextResistanceLevel(closePrice.Close, _average) + Convert.ToDecimal(atr.Last());
                }
                return StrategyWeight;
            }
            //check if long is trading sideways, need more entries to determin that!

            //check if long is in an uptrend.
            LogResult(0);
            return 0;
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
