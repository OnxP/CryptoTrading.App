using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class AroonOsc : IndicatorBaseDb
    {
        public double AroonOscValue { get; set; }
    }
    public class AroonOscIndicator : RunIndicatorBase<IndicatorContext<AroonOsc>, AroonOsc>
    {
        public override Indicator Indicator => Tulip.Indicators.aroonosc;

        public AroonOscIndicator(params decimal[] option) : base(option)
        {
        }

        protected override AroonOsc AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new AroonOsc() { CandleStickId = candlestickId, AroonOscValue = Convert.ToDouble(outputs[0]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
