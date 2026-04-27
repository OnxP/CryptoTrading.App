using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class EmaCciTradingStrategy : TradingStrategy
    {
        public EmaCciTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        protected override double StrategyWeight => 1.0;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            //add indicators to dictionary
            //for simple ema strat, we need slow fast and long, 13 21 and 200
            var ema = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 13 });
            var cci = new IndicatorSetUp(Tulip.Indicators.cci, new double[] { 40 });
            dict.Add("FastEma", ema);
            dict.Add("CCI", cci);
            return dict;
        }
        private int HLV { get; set; }
        private bool initial = true;
        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var high = indicatorOutputs["FastEma"][2].ToList();
            var low = indicatorOutputs["FastEma"][3].ToList();
            var cci = indicatorOutputs["CCI"][0].ToList();
            cci.Reverse();

            var Hlv = closePrice.Close > (decimal)high.Last() ? 1 : closePrice.Close < (decimal)low.Last() ? -1 : 0;
            if (initial)
            {
                HLV = Hlv;
                initial = false;
            }
            if (Hlv == 0 && Hlv == HLV)
            {
                return 0;
            }
            var sslUp = Hlv < 0 ? high.Last() : low.Last();
            var sslDown = Hlv > 0 ? high.Last() : low.Last();
            HLV = Hlv;
            var condition1 = sslUp > sslDown;
            var condition2 = cci.First() > -100;
            var condition3 = cci.Skip(1).First() < -100;
            var condition4 = cci.Skip(2).First() < -100;

            if (condition1 && condition2  && ( condition3 || condition4))
            {
                return SetSwingLowStopLimit(indicatorOutputs, closePrice, StopLimitTrackers)?1:0;
            }
            return 0;
        }
    }
}
