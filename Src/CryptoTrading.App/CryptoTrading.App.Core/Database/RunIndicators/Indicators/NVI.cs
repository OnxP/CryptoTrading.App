using CryptoTrading.App.Core.Database.Indicators;using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Nvi : IndicatorBaseDb
    {
        public decimal NviValue { get; set; }
    }
    public class NviIndicator : RunIndicatorBase<IndicatorContext<Nvi>, Nvi>
    {
        public override Indicator Indicator => Tulip.Indicators.nvi;

        public NviIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Nvi AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Nvi() { CandleStickId = candlestickId, NviValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
