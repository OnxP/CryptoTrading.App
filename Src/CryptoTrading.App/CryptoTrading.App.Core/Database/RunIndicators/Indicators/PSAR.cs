using CryptoTrading.App.Core.Database.Indicators;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Psar : IndicatorBaseDb
    {
        public decimal Step { get; set; }
        public decimal Max { get; set; }
    }
    public class PsarIndicator : RunIndicatorBase<IndicatorContext<Psar>, Psar>
    {

        public PsarIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Psar AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Psar() { CandleStickId = candlestickId, Step = outputs[0], Max = outputs[1] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
