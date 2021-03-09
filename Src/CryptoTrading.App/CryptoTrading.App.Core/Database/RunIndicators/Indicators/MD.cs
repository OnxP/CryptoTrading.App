using CryptoTrading.App.Core.Database.Indicators;using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Md : IndicatorBaseDb
    {
        public decimal MdValue { get; set; }
    }
    public class MdIndicator : RunIndicatorBase<IndicatorContext<Md>, Md>
    {
        public override Indicator Indicator => Tulip.Indicators.md;

        public MdIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Md AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Md() { CandleStickId = candlestickId, MdValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
