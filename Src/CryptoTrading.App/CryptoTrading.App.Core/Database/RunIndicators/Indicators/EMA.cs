using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Ema : IndicatorBaseDb
    {
        public double Period { get; set; }
        public double EmaValue { get; set; }
    }
    public class EmaIndicator : RunIndicatorBase<IndicatorContext<Ema>, Ema>
    {
        public override Indicator Indicator => Tulip.Indicators.ema;

        public EmaIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Ema AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Ema() { CandleStickId = candlestickId, Period = Convert.ToDouble(options[0]), EmaValue = Convert.ToDouble(outputs[0]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
