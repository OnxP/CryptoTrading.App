using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class StochRsi2 : IndicatorBaseDb
    {
        public double StochK { get; set; }
        public double StochD { get; set; }
    }
    public class StochRsi2Indicator : RunIndicatorBase<IndicatorContext<StochRsi2>, StochRsi2>
    {
        public override Indicator Indicator => Tulip.Indicators.stochrsi2;

        public StochRsi2Indicator(params decimal[] option) : base(option)
        {
        }

        protected override StochRsi2 AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new StochRsi2() { CandleStickId = candlestickId, StochK = Convert.ToDouble(outputs[0]), StochD = Convert.ToDouble(outputs[1])};
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
