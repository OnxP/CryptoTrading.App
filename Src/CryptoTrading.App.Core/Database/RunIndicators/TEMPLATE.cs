using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class TEMPLATE : IndicatorBaseDb
    {
        public double TEMPLATEValue { get; set; }
    }
    public class TEMPLATEIndicator : RunIndicatorBase<IndicatorContext<TEMPLATE>, TEMPLATE>
    {

        public TEMPLATEIndicator(params decimal[] option) : base(option)
        {
        }

        public override Indicator Indicator => throw new System.NotImplementedException();

        protected override TEMPLATE AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new TEMPLATE() { CandleStickId = candlestickId, TEMPLATEValue = Convert.ToDouble(outputs[0]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
