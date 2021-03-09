using CryptoTrading.App.Core.Database.Indicators;using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Cvi : IndicatorBaseDb
    {
        public decimal CviValue { get; set; }
    }
    public class CviIndicator : RunIndicatorBase<IndicatorContext<Cvi>, Cvi>
    {
        public override Indicator Indicator => Tulip.Indicators.cvi;

        public CviIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Cvi AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Cvi() { CandleStickId = candlestickId, CviValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
