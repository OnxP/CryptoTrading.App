using CryptoTrading.App.Core.Database.Indicators;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class AroonOsc : IndicatorBaseDb
    {
        public decimal AroonOscValue { get; set; }
    }
    public class AroonOscIndicator : RunIndicatorBase<IndicatorContext<AroonOsc>, AroonOsc>
    {

        public AroonOscIndicator(params decimal[] option) : base(option)
        {
        }

        protected override AroonOsc AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new AroonOsc() { CandleStickId = candlestickId, AroonOscValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
