using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public abstract class TradingStrategy : ITradingStrategy
    {
        public Dictionary<string, IndicatorSetUp> Indicators { get; }
        public int OutputLength { get; private set; }
        protected virtual double StrategyWeight => 1;
        public ILogger<TradingStrategy> Logger { get; }

        public StringBuilder Builder = new StringBuilder();
        public void Log(string v)
        {
            Builder.Append(v).Append(",\t");
        }
        protected void LogResult( int v)
        {
            Log($"Result: {v}");
            Builder.Append($"Weight: {StrategyWeight}");
            Logger.LogInformation(Builder.ToString());
            Builder.Clear();
        }
        protected void LogResult(DateTime closeTime, string symbol, decimal close, decimal targetPrice,
            decimal stopLimitPrice)
        {
            Builder.Append(
                $"DateTime: {closeTime:G}|Symbol: {symbol}| Close: {close:0.00000000}| Target: {targetPrice:0.00000000}| Stop: {stopLimitPrice:0.00000000}");
            Logger.LogInformation(Builder.ToString());
            Builder.Clear();
        }
        protected bool CheckLastTrade(System.DateTime endDateTime, System.DateTime closeTime, CandlestickInterval interval)
        {
            var nextTradeDate = CandleStickIntervalHelper.NextCandleStickTime(endDateTime, interval);
            return nextTradeDate < closeTime;
        }
        public List<double> GetIndicator(Dictionary<string, double[][]> indicatorOutputs, string indicator, int index)
        {
            var list = indicatorOutputs[indicator][index].ToList();
            if (indicator == "close") list.RemoveAll(x => x.Equals(0));
            list.Reverse();
            return list;
        }
        protected TradingStrategy(ILogger<TradingStrategy> logger)
        {
            Logger = logger;
            Indicators = GenerateIndicators();
            Indicators.Add("atr", new IndicatorSetUp(Tulip.Indicators.atr, new double[] { 14 }));
            int i = 0;
            foreach (var item in Indicators)
            {
                var optionLength = item.Value.Indicator.Start(item.Value.Options);
                if (optionLength == 0) optionLength = Convert.ToInt32(Math.Round(item.Value.Options[0], 0));
                i = Math.Max(optionLength, i);
            }

            OutputLength = i * 2;
            logger.LogInformation($"Strategy Initialisation complete for {this}, output length = {OutputLength}, Strategy Weight = {StrategyWeight}");
        }

        protected abstract Dictionary<string, IndicatorSetUp> GenerateIndicators();

        protected CandleStickDictionary ClosePrices { get; set; }

        public virtual double Calculate(CandleStickDictionary closePrices, IStopLimitTracker stopLimitTrackers)
            
        {
            Dictionary<string, double[][]> indicatorOutputs = new Dictionary<string, double[][]>();
            //load indicators
            foreach (var item in Indicators)
            {
                double[][] inputs = BuildInputs(item.Value,closePrices);                

                //Find output size and allocate output space.
                int output_length = (inputs[0].Length - item.Value.Indicator.Start(item.Value.Options));
                double[] output = new double[output_length];
                double[] output1 = new double[output_length];
                double[] output2 = new double[output_length];
                double[] output3 = new double[output_length];
                double[] output4 = new double[output_length];

                double[][] outputs = { output,output1,output2,output3,output4 };
                int success = item.Value.Indicator.Run(inputs, item.Value.Options, outputs);
                // log.
                indicatorOutputs.Add(item.Key, outputs);
            }

            var symbol = closePrices.Current.Symbol;
            Last10Low = closePrices.Values.OrderByDescending(x => x.OpenTime).Take(10).Min(x => x.Low); 
            Last5Low = closePrices.Values.OrderByDescending(x => x.OpenTime).Take(5).Min(x => x.Low);
            ClosePrices = closePrices;
            return Calculate(indicatorOutputs, closePrices.Current, stopLimitTrackers) * StrategyWeight;
        }
        //indicators work in reverse order, so the first item is the earliest candlestick.
        private double[][] BuildInputs(IndicatorSetUp indicator, CandleStickDictionary closePrices)
        {
            var candleSticks = closePrices.GroupCandleSticks(indicator.Multiplier);

            double[] close_prices = candleSticks.Select(x => (double)x.Close).ToArray();
            double[] volume = candleSticks.Select(x => (double)x.Volume).ToArray();
            double[] high = candleSticks.Select(x => (double)x.High).ToArray();
            double[] low = candleSticks.Select(x => (double)x.Low).ToArray();
            double[] open = candleSticks.Select(x => (double)x.Open).ToArray();
            var symbol = closePrices.Current.Symbol;
            return new double[][] { close_prices, volume, high, low,open };
        }

        protected decimal Last10Low { get; set; }
        protected decimal Last5Low { get; set; }

        //return +1 for buy Trade, -1 for sell, and 0 for Hold.
        protected abstract double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers);

        protected virtual bool SetStopLimit(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            if (!StopLimitTrackers.IsOpen)
            {
                //var diff = closePrice.Close - Last10Low;
                var atr = indicatorOutputs["atr"][0].ToList();
                //volume and profitability conditions.
                if (closePrice.QuoteAssetVolume > 2.0m && closePrice.NumberOfTrades > 10m && Symbol.Cache.Get(closePrice.Symbol).Price.Increment * 4 < 2 * (decimal)atr.Last())
                {
                    //if(diff < 3* Symbol.Cache.Get(closePrice.Symbol).Price.Increment) return false;
                    StopLimitTrackers.CurrentPrice = closePrice.Close;
                    StopLimitTrackers.Pair = closePrice.Symbol;
                    StopLimitTrackers.Multiple =
                        AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price,
                            (decimal)atr
                                .Last()); //(decimal)atr.Last() * 3;//diff + (2*Symbol.Cache.Get(closePrice.Symbol).Price.Increment); ;//AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price,multiple);
                    StopLimitTrackers.TargetPrice = AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price,
                        closePrice.Close + 3 * (decimal)atr.Last());
                    StopLimitTrackers.StopLimitPrice = AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price,
                        closePrice.Close - 3m * (decimal)atr.Last());

                    LogResult(closePrice.CloseTime, closePrice.Symbol, closePrice.Close, StopLimitTrackers.TargetPrice,
                        StopLimitTrackers.StopLimitPrice);
                    return true;

                }

                return false;
            }


            return false;
        }
        protected virtual bool SetSwingLowStopLimit(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            if (!StopLimitTrackers.IsOpen)
            {
                var diff = closePrice.Close - Last10Low;
                //var atr = indicatorOutputs["atr"][0].ToList();

                if (closePrice.QuoteAssetVolume > 2.0m && closePrice.NumberOfTrades > 10m &&
                    Symbol.Cache.Get(closePrice.Symbol).Price.Increment * 4 < 2 * diff)
                {
                    StopLimitTrackers.CurrentPrice = closePrice.Close;
                    StopLimitTrackers.Pair = closePrice.Symbol;
                    StopLimitTrackers.Multiple =
                        AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price,
                            1.5m * diff); //(decimal)atr.Last() * 3;//diff + (2*Symbol.Cache.Get(closePrice.Symbol).Price.Increment); ;//AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price,multiple);
                    StopLimitTrackers.TargetPrice = AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price,
                        closePrice.Close + 1.5m * (decimal)diff);
                    StopLimitTrackers.StopLimitPrice = AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price,
                        closePrice.Close - (decimal)diff);

                    LogResult(closePrice.CloseTime, closePrice.Symbol, closePrice.Close, StopLimitTrackers.TargetPrice,
                        StopLimitTrackers.StopLimitPrice);
                    return true;
                }

                return false;
            }

            return false;
        }
        protected void SetStopLimit(Candlestick closePrice, IStopLimitTracker stopLimitTrackers, double stopLimit, double target, double trail)
        {
            if (!stopLimitTrackers.IsOpen)
            {
                var targetPrice = AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price, (closePrice.Close - (decimal)stopLimit) * (decimal)target)/2;
                stopLimitTrackers.CurrentPrice = closePrice.Close;
                stopLimitTrackers.Pair = closePrice.Symbol;
                stopLimitTrackers.Multiple = AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price, (closePrice.Close - (decimal)stopLimit) * (decimal)target);
                stopLimitTrackers.TargetPrice = AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price, targetPrice + closePrice.Close);
                stopLimitTrackers.StopLimitPrice = AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price, (decimal)stopLimit);

                LogResult(closePrice.CloseTime, closePrice.Symbol, closePrice.Close, stopLimitTrackers.TargetPrice, stopLimitTrackers.StopLimitPrice);
            }
        }
        protected decimal AdjustForMinimum(InclusiveRange symbolQuantity, decimal calculateQuantity)
        {
            //return calculateQuantity;
            int precision = (int)Math.Round(-Math.Log10((double)symbolQuantity.Increment), 0);
            return Decimal.Round(calculateQuantity, precision, MidpointRounding.AwayFromZero);
        }
    }
}
