using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Dc : IndicatorBaseDb
    {
        public double Lower { get; set; }
        public double Middle { get; set; }
        public double Upper { get; set; }
    }
    public class DcIndicator : RunIndicatorBase<IndicatorContext<Dc>, Dc>
    {
        public override Indicator Indicator => Tulip.Indicators.dc;

        public DcIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Dc AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Dc() { CandleStickId = candlestickId, Lower = Convert.ToDouble(outputs[0]), Middle = Convert.ToDouble(outputs[1]), Upper = Convert.ToDouble(outputs[2]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
