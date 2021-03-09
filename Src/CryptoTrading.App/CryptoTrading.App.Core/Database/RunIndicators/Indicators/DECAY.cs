using CryptoTrading.App.Core.Database.Indicators;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Decay : IndicatorBaseDb
    {
        public decimal DecayValue { get; set; }
    }
    public class DecayIndicator : RunIndicatorBase<IndicatorContext<Decay>, Decay>
    {

        public DecayIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Decay AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Decay() { CandleStickId = candlestickId, DecayValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
