using CryptoTrading.App.Core.Database.Indicators;using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Vwap : IndicatorBaseDb
    {
        public decimal VwapValue { get; set; }
    }
    public class VwapIndicator : RunIndicatorBase<IndicatorContext<Vwap>, Vwap>
    {
        public override Indicator Indicator => Tulip.Indicators.vwap;

        public VwapIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Vwap AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Vwap() { CandleStickId = candlestickId, VwapValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
