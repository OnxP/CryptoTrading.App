using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class ZlEma : IndicatorBaseDb
    {
        public double Period { get; set; }

        public double ZlEmaValue { get; set; }
    }
    public class ZlEmaIndicator : RunIndicatorBase<IndicatorContext<ZlEma>, ZlEma>
    {
        public override Indicator Indicator => Tulip.Indicators.zlema;

        public ZlEmaIndicator(params decimal[] option) : base(option)
        {
        }

        protected override ZlEma AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new ZlEma() { CandleStickId = candlestickId, ZlEmaValue = Convert.ToDouble(outputs[0]), Period = Convert.ToDouble(options[0]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
