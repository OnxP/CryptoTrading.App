using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Decay : IndicatorBaseDb
    {
        public double DecayValue { get; set; }
    }
    public class DecayIndicator : RunIndicatorBase<IndicatorContext<Decay>, Decay>
    {
        public override Indicator Indicator => Tulip.Indicators.decay;

        public DecayIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Decay AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Decay() { CandleStickId = candlestickId, DecayValue = Convert.ToDouble(outputs[0]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
