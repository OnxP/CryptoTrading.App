using CryptoTrading.App.Core.Database.Indicators;using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Macd : IndicatorBaseDb
    {
        public decimal ShortPeriod { get; set; }
        public decimal LongPeriod { get; set; }
        public decimal Histogram { get; set; }
    }
    public class MacdIndicator : RunIndicatorBase<IndicatorContext<Macd>, Macd>
    {
        public override Indicator Indicator => Tulip.Indicators.macd    ;

        public MacdIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Macd AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Macd() { CandleStickId = candlestickId, ShortPeriod = outputs[0], LongPeriod = outputs[1], Histogram = outputs[2] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
