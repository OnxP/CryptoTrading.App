using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Macd : IndicatorBaseDb
    {
        public string Period { get; set; }
        public double MacdLine { get; set; }
        public double SignalLine { get; set; }
        public double Histogram { get; set; }
    }
    public class MacdIndicator : RunIndicatorBase<IndicatorContext<Macd>, Macd>
    {
        public override Indicator Indicator => Tulip.Indicators.macd    ;

        public MacdIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Macd AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Macd() { CandleStickId = candlestickId,Period = string.Join(',',options), MacdLine = Convert.ToDouble(outputs[0]), SignalLine = Convert.ToDouble(outputs[1]), Histogram = Convert.ToDouble(outputs[2]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
