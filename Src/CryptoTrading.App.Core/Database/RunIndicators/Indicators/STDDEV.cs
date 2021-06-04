using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Stddev : IndicatorBaseDb
    {
        public double StddevValue { get; set; }
    }
    public class StddevIndicator : RunIndicatorBase<IndicatorContext<Stddev>, Stddev>
    {
        public override Indicator Indicator => Tulip.Indicators.stddev;

        public StddevIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Stddev AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Stddev() { CandleStickId = candlestickId, StddevValue = Convert.ToDouble(outputs[0]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
