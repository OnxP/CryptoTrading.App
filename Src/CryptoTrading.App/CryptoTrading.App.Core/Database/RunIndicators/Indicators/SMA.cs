using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Sma : IndicatorBaseDb
    {
        public double Period { get; set; }
        public double SmaValue { get; set; }
    }
    public class SmaIndicator : RunIndicatorBase<IndicatorContext<Sma>, Sma>
    {
        public override Indicator Indicator => Tulip.Indicators.sma;

        public SmaIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Sma AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Sma() { CandleStickId = candlestickId, Period = Convert.ToDouble(options[0]), SmaValue = Convert.ToDouble(outputs[0]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
