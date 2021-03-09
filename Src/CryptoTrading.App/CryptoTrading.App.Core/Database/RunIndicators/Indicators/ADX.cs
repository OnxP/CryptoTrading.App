using CryptoTrading.App.Core.Database.Indicators;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Adx : IndicatorBaseDb
    {
        public decimal AdxValue { get; set; }
    }
    public class AdxIndicator : RunIndicatorBase<IndicatorContext<Adx>, Adx>
    {

        public AdxIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Adx AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Adx() { CandleStickId = candlestickId, AdxValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
