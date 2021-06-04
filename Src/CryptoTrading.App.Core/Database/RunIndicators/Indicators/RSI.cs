using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Rsi : IndicatorBaseDb
    {
        public double RsiValue { get; set; }
    }
    public class RsiIndicator : RunIndicatorBase<IndicatorContext<Rsi>, Rsi>
    {
        public override Indicator Indicator => Tulip.Indicators.rsi;

        public RsiIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Rsi AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Rsi() { CandleStickId = candlestickId, RsiValue = Convert.ToDouble(outputs[0]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
