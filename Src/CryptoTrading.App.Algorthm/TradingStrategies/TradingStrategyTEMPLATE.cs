using System;
using System.Collections.Generic;
using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class TradingStrategyTEMPLATE : TradingStrategy
    {
        public TradingStrategyTEMPLATE(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            throw new NotImplementedException();
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            throw new NotImplementedException();
        }
    }
}
