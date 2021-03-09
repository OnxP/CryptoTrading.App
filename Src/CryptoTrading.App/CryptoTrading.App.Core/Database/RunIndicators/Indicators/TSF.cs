using CryptoTrading.App.Core.Database.Indicators;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Tsf : IndicatorBaseDb
    {
        public decimal TsfValue { get; set; }
    }
    public class TsfIndicator : RunIndicatorBase<IndicatorContext<Tsf>, Tsf>
    {

        public TsfIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Tsf AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Tsf() { CandleStickId = candlestickId, TsfValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
