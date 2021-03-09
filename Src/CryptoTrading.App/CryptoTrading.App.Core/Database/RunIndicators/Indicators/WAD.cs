using CryptoTrading.App.Core.Database.Indicators;using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Wad : IndicatorBaseDb
    {
        public decimal WadValue { get; set; }
    }
    public class WadIndicator : RunIndicatorBase<IndicatorContext<Wad>, Wad>
    {
        public override Indicator Indicator => Tulip.Indicators.wad;

        public WadIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Wad AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Wad() { CandleStickId = candlestickId, WadValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
