using CryptoTrading.App.Core.Database.Indicators;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Ema : IndicatorBaseDb
    {
        public decimal Period { get; set; }
        public decimal EmaValue { get; set; }
    }
    public class EmaIndicator : RunIndicatorBase<IndicatorContext<Ema>, Ema>
    {

        public EmaIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Ema AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Ema() { CandleStickId = candlestickId, Period = options[0], EmaValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
