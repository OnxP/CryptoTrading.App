using System;
using System.Collections.Generic;
using System.Text;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public class EmaTradingStrategy : TradingStrategy
    {
        protected override Dictionary<string, (Indicator indicator, double[] options)> GenerateIndicators()
        {
            var dict = new Dictionary<string, (Indicator indicator, double[] options)>();

            //add indicators to dictionary

            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs)
        {
            throw new NotImplementedException();
        }
    }
}
