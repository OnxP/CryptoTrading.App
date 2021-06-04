using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Vwma : IndicatorBaseDb
    {
        public double VwmaValue { get; set; }
    }
    public class VwmaIndicator : RunIndicatorBase<IndicatorContext<Vwma>, Vwma>
    {
        public override Indicator Indicator => Tulip.Indicators.vwma;

        public VwmaIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Vwma AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Vwma() { CandleStickId = candlestickId, VwmaValue = Convert.ToDouble(outputs[0]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
