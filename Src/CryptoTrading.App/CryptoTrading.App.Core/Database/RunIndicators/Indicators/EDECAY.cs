using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class EDecay : IndicatorBaseDb
    {
        public double EDecayValue { get; set; }
    }
    public class EDecayIndicator : RunIndicatorBase<IndicatorContext<EDecay>, EDecay>
    {
        public override Indicator Indicator => Tulip.Indicators.edecay;

        public EDecayIndicator(params decimal[] option) : base(option)
        {
        }

        protected override EDecay AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new EDecay() { CandleStickId = candlestickId, EDecayValue = Convert.ToDouble(outputs[0]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
