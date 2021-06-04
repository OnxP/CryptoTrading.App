using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Wacd : IndicatorBaseDb
    {
        public string Period { get; set; }
        public double WacdLine { get; set; }
        public double SignalLine { get; set; }
        public double Histogram { get; set; }
    }
    public class WacdIndicator : RunIndicatorBase<IndicatorContext<Wacd>, Wacd>
    {
        public override Indicator Indicator => Tulip.Indicators.wacd    ;

        public WacdIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Wacd AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Wacd() { CandleStickId = candlestickId,Period = string.Join(',',options), WacdLine = Convert.ToDouble(outputs[0]), SignalLine = Convert.ToDouble(outputs[1]), Histogram = Convert.ToDouble(outputs[2]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
