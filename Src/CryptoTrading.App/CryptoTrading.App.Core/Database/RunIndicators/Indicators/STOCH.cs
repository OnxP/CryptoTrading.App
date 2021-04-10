using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Stoch : IndicatorBaseDb
    {
        public double StochK { get; set; }
        public double StochD { get; set; }
    }
    public class StochIndicator : RunIndicatorBase<IndicatorContext<Stoch>, Stoch>
    {
        public override Indicator Indicator => Tulip.Indicators.stoch;

        public StochIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Stoch AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Stoch() { CandleStickId = candlestickId, StochK = Convert.ToDouble(outputs[0]), StochD = Convert.ToDouble(outputs[1])};
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
