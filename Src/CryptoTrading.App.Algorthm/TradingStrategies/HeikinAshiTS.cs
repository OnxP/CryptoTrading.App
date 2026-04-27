using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class HeikinAshiTS : TradingStrategy
    {


        public HeikinAshiTS(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        public HeikinAshiTS(ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
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
            var ema50 = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 50 });
            var close = new IndicatorSetUp(Tulip.Indicators.close, new double[] { 40 });
            var srsi = new IndicatorSetUp(Tulip.Indicators.stochrsi2, new double[] { 14, 14, 3, 3 });

            dict.Add("MediumWma", ema50);
            dict.Add("SRSI", srsi);
            dict.Add("close", close);
            return dict;
        }
        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice,
            IStopLimitTracker StopLimitTrackers)
        {
            var mediumWma = indicatorOutputs["MediumWma"][0].ToList();
            var kLine = indicatorOutputs["SRSI"][0].ToList();
            var dLine = indicatorOutputs["SRSI"][1].ToList();
            var close = indicatorOutputs["close"][0].ToList();
            var volume = indicatorOutputs["close"][1].ToList();
            var high = indicatorOutputs["close"][2].ToList();
            var low = indicatorOutputs["close"][3].ToList();
            var open = indicatorOutputs["close"][4].ToList();

            kLine.Reverse();
            dLine.Reverse();

            var condition1 = mediumWma.Last() < (double)closePrice.Close;
            var condition2 = kLine.First() > dLine.First() && kLine.First() < 50 && kLine.Skip(1).First() > dLine.Skip(1).First();
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

            var entryCondition = hAlist.Last().IsBullish && hAlist.Last().Open == closePrice.Low;

            if (condition1 && condition2 && entryCondition)
            {
                SetStopLimit(indicatorOutputs, closePrice, StopLimitTrackers);
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


        protected override bool SetStopLimit(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice,
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
}
