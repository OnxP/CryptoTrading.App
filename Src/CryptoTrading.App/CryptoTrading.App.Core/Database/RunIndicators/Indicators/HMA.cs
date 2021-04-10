using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Hma : IndicatorBaseDb
    {
        public double Period { get; set; }

        public double HmaValue { get; set; }
    }
    public class HmaIndicator : RunIndicatorBase<IndicatorContext<Hma>, Hma>
    {
        public override Indicator Indicator => Tulip.Indicators.hma;

        public HmaIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Hma AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Hma() { CandleStickId = candlestickId, HmaValue = Convert.ToDouble(outputs[0]), Period = Convert.ToDouble(options[0]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
