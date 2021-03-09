using CryptoTrading.App.Core.Database.Indicators;using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Ao : IndicatorBaseDb
    {
        public decimal AoValue { get; set; }
    }
    public class AoIndicator : RunIndicatorBase<IndicatorContext<Ao>, Ao>
    {
        public override Indicator Indicator => Tulip.Indicators.ao;

        public AoIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Ao AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Ao() { CandleStickId = candlestickId, AoValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
