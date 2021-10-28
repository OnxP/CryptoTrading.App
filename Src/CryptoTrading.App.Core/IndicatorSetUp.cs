using System;
using System.Collections.Generic;
using System.Text;
using Tulip;

namespace CryptoTrading.App.Core
{
    public class IndicatorSetUp
    {
        public IndicatorSetUp(Indicator macd, double[] vs)
        {
        }

        public Indicator Indicator { get; set; }
        public double[] Options { get; set; }
        public int Multiplier { get; set; } = 1;
    }
}
