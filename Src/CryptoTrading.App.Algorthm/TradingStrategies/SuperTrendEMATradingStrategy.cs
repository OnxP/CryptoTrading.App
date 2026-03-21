using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Data.Entity.Core.Metadata.Edm;
using System.Linq;
using System.Threading;

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
            var ema = new IndicatorSetUp(Tulip.Indicators.hma, new double[] { 200 });
            var adx = new IndicatorSetUp(Tulip.Indicators.supertrend, new double[] { 12,2 });
            var adxlong = new IndicatorSetUp(Tulip.Indicators.supertrend, new double[] {  12,3 });
            dict.Add("LongEma", ema);
            dict.Add("superTrend", adx);
            dict.Add("superTrendLong", adxlong);
            return dict;
        }

        private bool SuperTrendLongFlag = false;
        private bool SuperTrendShortGreenFlag = false;
        private bool SuperTrendShortRedFlag = false;
        private bool SuperTrendShortGreen2Flag = false;
        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var ema = indicatorOutputs["LongEma"][0].ToList();
            var superTrend = indicatorOutputs["superTrend"][0].ToList();
            var direction = indicatorOutputs["superTrend"][1].ToList();
            var superTrendLong = indicatorOutputs["superTrendLong"][0].ToList();
            var directionLong = indicatorOutputs["superTrendLong"][1].ToList();

            //green super long super trend, only continue.
            if (directionLong.Last() != -1)
            {
                Reset();
                return 0;
            }

            if (directionLong.Last() == -1) SuperTrendLongFlag = true;

            if (SuperTrendLongFlag && !SuperTrendShortGreenFlag && direction.Last() == -1)
                SuperTrendShortGreenFlag = true;

            if (SuperTrendLongFlag && SuperTrendShortGreenFlag && !SuperTrendShortRedFlag && direction.Last() == 1)
                SuperTrendShortRedFlag = true;

            if (SuperTrendLongFlag && SuperTrendShortGreenFlag && SuperTrendShortRedFlag && !SuperTrendShortGreen2Flag && direction.Last() == -1)
                SuperTrendShortGreen2Flag = true;
            else
            {
                SuperTrendShortGreen2Flag = false;
            }

            //close > open

            var condition4 = (double)closePrice.Close > ema.Last() && closePrice.Close > closePrice.Open;

            var res = StopLimitTrackers.SetStopLimit(indicatorOutputs, closePrice, SuperTrendShortGreen2Flag && condition4,
                s => Logger.LogInformation(s));
            if (res!=0) Reset();
            return res;
        }

        private void Reset()
        {
            SuperTrendLongFlag = false;
            SuperTrendShortGreenFlag = false;
            SuperTrendShortRedFlag = false;
            SuperTrendShortGreen2Flag = false;
        }
    }
}
