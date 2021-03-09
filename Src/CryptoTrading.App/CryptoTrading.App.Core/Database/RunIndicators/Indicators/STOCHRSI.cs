using CryptoTrading.App.Core.Database.Indicators;using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class StochRsi : IndicatorBaseDb
    {
        public decimal StochRsiValue { get; set; }
    }
    public class StochRsiIndicator : RunIndicatorBase<IndicatorContext<StochRsi>, StochRsi>
    {
        public override Indicator Indicator => Tulip.Indicators.stochrsi;

        public StochRsiIndicator(params decimal[] option) : base(option)
        {
        }

        protected override StochRsi AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new StochRsi() { CandleStickId = candlestickId, StochRsiValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
