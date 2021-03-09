using CryptoTrading.App.Core.Database.Indicators;using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class ZlEma : IndicatorBaseDb
    {
        public decimal ZlEmaValue { get; set; }
    }
    public class ZlEmaIndicator : RunIndicatorBase<IndicatorContext<ZlEma>, ZlEma>
    {
        public override Indicator Indicator => Tulip.Indicators.zlema;

        public ZlEmaIndicator(params decimal[] option) : base(option)
        {
        }

        protected override ZlEma AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new ZlEma() { CandleStickId = candlestickId, ZlEmaValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
