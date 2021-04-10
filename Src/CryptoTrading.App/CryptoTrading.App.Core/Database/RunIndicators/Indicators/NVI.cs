using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Nvi : IndicatorBaseDb
    {
        public double NviValue { get; set; }
    }
    public class NviIndicator : RunIndicatorBase<IndicatorContext<Nvi>, Nvi>
    {
        public override Indicator Indicator => Tulip.Indicators.nvi;

        public NviIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Nvi AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Nvi() { CandleStickId = candlestickId, NviValue = Convert.ToDouble(outputs[0]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
