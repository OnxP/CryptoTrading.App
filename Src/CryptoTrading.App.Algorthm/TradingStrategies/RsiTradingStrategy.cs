using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class RsiTradingStrategy : TradingStrategy
    {
        public RsiTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        protected override double StrategyWeight => 1.0;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            //add indicators to dictionary
            var rsi = new IndicatorSetUp(Tulip.Indicators.rsi, new double[] { 14 });
            //var ema = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 100 });
            dict.Add("Rsi", rsi);
            //dict.Add("LongEma", ema);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var rsi = indicatorOutputs["Rsi"][0].ToList();
            //var longEma = indicatorOutputs["LongEma"][0].ToList();

            Log($"RSI: {rsi.Last()}");
            Log($"RSI: {rsi.IndexOf(rsi.Count - 2)}");
            //Log($"Long Ema: {longEma.Last()}");
            Log($"Close Price: {closePrice}");

            // log values
            
            var condition1 = rsi.Last() > 20;
            var condition2 = rsi.IndexOf(rsi.Count-2) < 20; 
            //Price > than Long EMA
            //Long EMA is in an uptrend
            //Fast > Slow EMA
            if (condition1 && condition2)
            {
                SetStopLimit(indicatorOutputs, closePrice, StopLimitTrackers);
                return 1;
            }
            return 0;
        }

        
    }
}
