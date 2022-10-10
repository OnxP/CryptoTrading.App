using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class SuperTrendEMATradingStrategy : TradingStrategy
    {
        public SuperTrendEMATradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        protected override double StrategyWeight => 1.0;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            //add indicators to dictionary
            //for simple ema strat, we need slow fast and long, 13 21 and 200
            var ema = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 200 });
            var atr10 = new IndicatorSetUp(Tulip.Indicators.atr, new double[] { 10 });
            var adx = new IndicatorSetUp(Tulip.Indicators.rsi, new double[] { 14 });
            dict.Add("LongEma", ema);
            dict.Add("ATR10", atr10);
            dict.Add("ADX", adx);
            return dict;
        }
        private bool initial = true;
        private bool Trend = true;
        private double up1;
        private double dn1;
        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var ema = indicatorOutputs["LongEma"][0].ToList();
            var atr10 = indicatorOutputs["ATR10"][0].ToList();
            var atr = indicatorOutputs["atr"][0].ToList();
            var adx = indicatorOutputs["ADX"][0].ToList();

            var up = (double)(closePrice.High + closePrice.Low) / 2 - (3 * atr10.Last());
            var dn = (double)(closePrice.High + closePrice.Low) / 2 + (3 * atr10.Last());

            if (initial)
            {
                up1 = up;
                dn1 = dn;
                Trend = Trend==false && (double)closePrice.Close > dn1 || (Trend != true || !((double)closePrice.Close < up1)) && Trend;
                initial = false;
                return 0;
            }
            var trend = Trend == false && (double)closePrice.Close > dn1 || (Trend != true || !((double)closePrice.Close < up1)) && Trend;

            var condition1 = trend;// && Trend == false;
            var condition4 = (double)closePrice.Close > ema.Last();
            up1 = up;
            dn1 = dn;
            Trend = trend;

            return StopLimitTrackers.SetStopLimit(indicatorOutputs, closePrice,  condition1 && condition4 && adx.Last()>65,
                s => Logger.LogInformation(s));
        }
    }
}
