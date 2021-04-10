using CryptoTrading.App.Core.Database.Indicators;
using System;
using Tulip;

namespace CryptoTrading.App.Core.Database.RunIndicators.Indicators
{
    public class Psar : IndicatorBaseDb
    {
        public double PsarValue { get; set; }
    }
    public class PsarIndicator : RunIndicatorBase<IndicatorContext<Psar>, Psar>
    {
        public override Indicator Indicator => Tulip.Indicators.psar;

        public PsarIndicator(params decimal[] option) : base(option)
        {
        }

        protected override Psar AddToDb(int candlestickId, params decimal[] outputs)
        {
            return new Psar() { CandleStickId = candlestickId, PsarValue = Convert.ToDouble(outputs[0]) };
        }
        protected override void SaveContext()
        {
            context.Values.AddRange(list);
        }
    }
}
