using CryptoTrading.App.Core.Database.Indicators;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Rsi : IndicatorBaseDb
    {
        public decimal RsiValue { get; set; }
    }
    public class RsiIndicator : RunIndicatorBase<IndicatorContext<Rsi>, Rsi>
    {

        public RsiIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Rsi AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Rsi() { CandleStickId = candlestickId, RsiValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
