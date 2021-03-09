using CryptoTrading.App.Core.Database.Indicators;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Aroon : IndicatorBaseDb
    {
        public decimal Up { get; set; }
        public decimal Down { get; set; }
    }
    public class AroonIndicator : RunIndicatorBase<IndicatorContext<Aroon>, Aroon>
    {

        public AroonIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Aroon AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Aroon() { CandleStickId = candlestickId, Up = outputs[0], Down = outputs[1] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
