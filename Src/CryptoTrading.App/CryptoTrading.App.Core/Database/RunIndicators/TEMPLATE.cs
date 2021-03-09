using CryptoTrading.App.Core.Database.Indicators;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class TEMPLATE : IndicatorBaseDb
    {
        public decimal TEMPLATEValue { get; set; }
    }
    public class TEMPLATEIndicator : RunIndicatorBase<IndicatorContext<TEMPLATE>, TEMPLATE>
    {

        public TEMPLATEIndicator(params decimal[] option) : base(option)
        {
        }

        protected override TEMPLATE AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new TEMPLATE() { CandleStickId = candlestickId, TEMPLATEValue = outputs[0] };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
