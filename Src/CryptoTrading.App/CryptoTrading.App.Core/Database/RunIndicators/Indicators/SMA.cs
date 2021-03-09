using CryptoTrading.App.Core.Database.Indicators;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Sma : IndicatorBaseDb
    {
        public decimal Period { get; set; }
        public decimal SmaValue { get; set; }
    }
    public class SmaIndicator : RunIndicatorBase<IndicatorContext<Sma>, Sma>
    {

        public SmaIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Sma AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Sma() { CandleStickId = candlestickId, Period = options[0], SmaValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
