using CryptoTrading.App.Core.Database.Indicators;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Bbands : IndicatorBaseDb
    {
        public decimal Lower { get; set; }
        public decimal Middle { get; set; }
        public decimal Upper { get; set; }
    }
    public class BbandsIndicator : RunIndicatorBase<IndicatorContext<Bbands>, Bbands>
    {

        public BbandsIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Bbands AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Bbands() { CandleStickId = candlestickId, Lower = outputs[0], Middle = outputs[1], Upper = outputs[2] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
