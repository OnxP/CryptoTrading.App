using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Aroon : IndicatorBaseDb
    {
        public double Up { get; set; }
        public double Down { get; set; }
    }
    public class AroonIndicator : RunIndicatorBase<IndicatorContext<Aroon>, Aroon>
    {
        public override Indicator Indicator => Tulip.Indicators.aroon;

        public AroonIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Aroon AddToDb(int candlestickId, params decimal[] outputs)
        {//need to re run this indicator for queries to work.
            return new Aroon() { CandleStickId = candlestickId, Down = Convert.ToDouble(outputs[0]), Up = Convert.ToDouble(outputs[1]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
