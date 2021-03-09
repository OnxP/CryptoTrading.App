using CryptoTrading.App.Core.Database.Indicators;using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Stddev : IndicatorBaseDb
    {
        public decimal StddevValue { get; set; }
    }
    public class StddevIndicator : RunIndicatorBase<IndicatorContext<Stddev>, Stddev>
    {
        public override Indicator Indicator => Tulip.Indicators.stddev;

        public StddevIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Stddev AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Stddev() { CandleStickId = candlestickId, StddevValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
