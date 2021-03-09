using CryptoTrading.App.Core.Database.Indicators;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class EDecay : IndicatorBaseDb
    {
        public decimal EDecayValue { get; set; }
    }
    public class EDecayIndicator : RunIndicatorBase<IndicatorContext<EDecay>, EDecay>
    {

        public EDecayIndicator(params decimal[] option) : base(option)
        {
        }

        protected override EDecay AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new EDecay() { CandleStickId = candlestickId, EDecayValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
