using CryptoTrading.App.Core.Exchange;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.CustomIndicators
{
    public enum SignalType
    {
        None,
        Buy,
        Sell
    }

    public class Indicator
    {
        public Indicator(string name)
        {
            Name = name;
        }

        public string Name { get; set; }
        public bool IsValid { get; set; }
    }
public class Signal
{
        public List<ExchangeCandlestick> Candles { get; set; }
        public SignalType Type { get; set; }
        public List<Indicator> Indicators { get; set; }
        public decimal CloseAtPrice { get; set; }

        public int Count
        {
            get
            {
                return Indicators.Count(e => e.IsValid);
            }
        }

        public Signal()
        {
            Indicators = new List<Indicator>();
}

        public void Reset(List<ExchangeCandlestick> Candles)
        {
            Type = SignalType.None;
            foreach (var indicator in Indicators)
            {
                indicator.IsValid = false;
            }
        }
    }
}
