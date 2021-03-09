using CryptoTrading.App.Core.Database.Indicators;using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Wma : IndicatorBaseDb
    {
        public decimal WmaValue { get; set; }
    }
    public class WmaIndicator : RunIndicatorBase<IndicatorContext<Wma>, Wma>
    {
        public override Indicator Indicator => Tulip.Indicators.wma;

        public WmaIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Wma AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Wma() { CandleStickId = candlestickId, WmaValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
