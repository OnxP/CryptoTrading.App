using System;
using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualBasic;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class SimpleVolumeStrategy : TradingStrategy
    {
        public SimpleVolumeStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        public SimpleVolumeStrategy(ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
        {
            noOfTrades = NoOfTrades;
        }

        protected override double StrategyWeight => 1.0 / noOfTrades;

        private double noOfTrades = 1d;
        //protected override double StrategyWeight => 1.0;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            //add indicators to dictionary
            var ema200 = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 200 });
            var ema50 = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 50 });
            var adx = new IndicatorSetUp(Tulip.Indicators.adx, new double[] { 14, 14 });
            var close = new IndicatorSetUp(Tulip.Indicators.close, new double[] { 40 });

            dict.Add("LongWma", ema200);
            dict.Add("MediumWma", ema50);
            dict.Add("ADX", adx);
            dict.Add("close", close);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice,
            IStopLimitTracker StopLimitTrackers)
        {
            var longWma = indicatorOutputs["LongWma"][0].ToList();
            var mediumWma = indicatorOutputs["MediumWma"][0].ToList();
            var adx = indicatorOutputs["ADX"][0].ToList();
            var close = indicatorOutputs["close"][0].ToList();
            var volume = indicatorOutputs["close"][1].ToList();
            var high = indicatorOutputs["close"][2].ToList();
            var low = indicatorOutputs["close"][3].ToList();
            var open = indicatorOutputs["close"][4].ToList();

            //volume.Reverse();

            var condition1 = mediumWma.Last() > longWma.Last();
            var condition2 = volume.First() > volume.Skip(1).First();
            var condition4 = adx.Last() > 25;
            //Add range detection, if the last 10 candlestisk are in a range of 3 * interval then exclude trades.
            //need to add doji detection. wicks vs open and close. small wicks tighter the range on Open and close, large wicks more range.
            //also may want to set the sl to be a max -2% of the close

            //Price has to be above the 50EMA
            var diff = closePrice.High - closePrice.Low;
            var diffCondition = diff > 4 * Symbol.Cache.Get(closePrice.Symbol).Price.Increment;
            //Price > than Long EMA
            //Long EMA is in an uptrend
            //Fast > Slow EMA
            var hAlist = generateHeikinAshiCandleSticks(open,high,low,close);
            if (StopLimitTrackers.IsOpen && hAlist.Last().IsBareish)
            {
                StopLimitTrackers.ManualChangeSL(closePrice.Close);
            }

            var condition3 = hAlist.Last().IsBullish;
            var condition5 = mediumWma.Last() >= low[0];
            var condition6 = mediumWma.Last() >= low[1];
            var condition7 = close[0] > mediumWma.Last() ||close[1] > mediumWma.Last() ||close[2] > mediumWma.Last() ||close[3] > mediumWma.Last();
            if (condition1 && condition2 && condition3 && condition4 && (condition5 || condition6) && condition7 && diffCondition)
            {
                if (!StopLimitTrackers.IsOpen)
                {
                    StopLimitTrackers.CurrentPrice = closePrice.Close;
                    StopLimitTrackers.Pair = closePrice.Symbol;
                    StopLimitTrackers.TargetPrice = AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price, closePrice.Close * 8);
                    StopLimitTrackers.StopLimitPrice = AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price, hAlist.Last(x=>x.IsBareish).ClosePrice);
                }

                LogResult(closePrice.CloseTime, closePrice.Symbol, closePrice.Close, StopLimitTrackers.TargetPrice,
                    StopLimitTrackers.StopLimitPrice);

                return 1;
            }

            return 0;
        }

        private List<HeikinAshi> generateHeikinAshiCandleSticks(List<double> open, List<double> high, List<double> low, List<double> close)
        {
            List<HeikinAshi> list = new List<HeikinAshi>();
            for (int i = close.Count-2; i >=0; i--)
            {
                list.Add(new HeikinAshi(open[i], close[i], high[i], low[i], open.Skip(i + 1).First(), close.Skip(i + 1).First()));
            }

            return list;
        }


        protected override bool SetStopLimit(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice,
            IStopLimitTracker StopLimitTrackers)
        {
            if (!StopLimitTrackers.IsOpen)
            {
                var close = indicatorOutputs["close"][0].ToList();
                var volume = indicatorOutputs["close"][1].ToList();
                var high = indicatorOutputs["close"][2].ToList();
                var low = indicatorOutputs["close"][3].ToList();
                var open = indicatorOutputs["close"][4].ToList();
                StopLimitTrackers.CurrentPrice = closePrice.Close;
                StopLimitTrackers.Pair = closePrice.Symbol;
                StopLimitTrackers.TargetPrice = AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price,
                    closePrice.Close + closePrice.Close);
                decimal sl = default;
                for (int i = 0; i < close.Count-1; i++)
                {
                    var heikinAshi = new HeikinAshi(open[i],close[i],high[i],low[i], open.Skip(i+1).First(), close.Skip(i+1).First());
                    if (heikinAshi.Close < heikinAshi.Open)// && (heikinAshi.Open - heikinAshi.Close > Symbol.Cache.Get(closePrice.Symbol).Price.Increment*2))
                    {
                        sl = (decimal)low[i];
                        break;
                    }
                }

                StopLimitTrackers.StopLimitPrice = AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price, sl);
                return true;
            }

            return false;
        }
    }

    public class HeikinAshi
    {
        public HeikinAshi(Candlestick closePrice, double open,double close)
        {
            ClosePrice = (decimal)close;
            Open = 0.5m * (decimal)(close + open);
            Close = 0.25m * (closePrice.Close + closePrice.Open + closePrice.Low + closePrice.High);
        }
        public HeikinAshi(double open, double close, double high, double low, double prevOpen, double prevClose)
        {
            ClosePrice = (decimal)close;
            Open = 0.5m * (decimal)(prevOpen + prevClose);
            Close = 0.25m * (decimal)(close + open + low + high);
        }
        public decimal Open { get; }
        public decimal Close { get; }
        public decimal ClosePrice { get; }
        public bool IsBullish => Close > Open;
        public bool IsBareish => Close < Open;
    }
}
