using CryptoTrading.App.Core.Database.Indicators;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Hma : IndicatorBaseDb
    {
        public decimal HmaValue { get; set; }
    }
    public class HmaIndicator : RunIndicatorBase<IndicatorContext<Hma>, Hma>
    {

        public HmaIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Hma AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Hma() { CandleStickId = candlestickId, HmaValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
