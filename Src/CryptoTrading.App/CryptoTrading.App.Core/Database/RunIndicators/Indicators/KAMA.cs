using CryptoTrading.App.Core.Database.Indicators;using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Kama : IndicatorBaseDb
    {
        public decimal KamaValue { get; set; }
    }
    public class KamaIndicator : RunIndicatorBase<IndicatorContext<Kama>, Kama>
    {
        public override Indicator Indicator => Tulip.Indicators.kama;

        public KamaIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Kama AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Kama() { CandleStickId = candlestickId, KamaValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
