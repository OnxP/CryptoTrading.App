using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Vwap : IndicatorBaseDb
    {
        public double Period { get; set; }
        public double VwapValue { get; set; }
    }
    public class Vsapindicator : RunIndicatorBase<IndicatorContext<Vwap>, Vwap>
    {
        public override Indicator Indicator => Tulip.Indicators.vwap;

        public Vsapindicator(params decimal[] option) : base(option)
        {
        }

        protected override Vwap AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Vwap() { CandleStickId = candlestickId,Period = Convert.ToDouble(options[0]), VwapValue = Convert.ToDouble(outputs[0]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
