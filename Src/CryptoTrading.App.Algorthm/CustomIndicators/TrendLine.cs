using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Algorthm.CustomIndicators
{
    public class TrendLine
    {
        private decimal[] _trendNone;
        private decimal[] _trendRed;
        private decimal _filterNumber;
        private decimal _period;

        public TrendLine(decimal filterNumber = 3, decimal period = 10)
        {
            _filterNumber = filterNumber;
            _period = period;
        }

//--------------------------------------------------------------------
        private decimal TrendMA(List<Candlestick> Candles, int startBar, int period)
        {
            return MovingAverage.Get(Candles, startBar, period, MaMethod.LWMA, AppliedPrice.Close);
        }

        //--------------------------------------------------------------------
        public bool IsGreen(int bar)
        {
            if (bar < 0 || bar >= _trendRed.Length) return false;
            var x0 = bar > 0 ? _trendRed[bar - 1] : 0;
            var x1 = _trendRed[bar];
            var x2 = _trendRed[bar + 1];
            if (x0 >= x1) return true;
            if (x0 >= x2) return true;
            return false;
        }

        //--------------------------------------------------------------------
        public bool IsRed(int bar)
        {
            if (bar < 0 || bar >= _trendRed.Length) return false;
            var x0 = bar > 0 ? _trendRed[bar - 1] : 0;
            var x1 = _trendRed[bar];
            var x2 = _trendRed[bar + 1];
            if (x0 <= x1) return true;
            if (x0 <= x2) return true;
            return false;
        }

//--------------------------------------------------------------------
        public void Refresh(List<Candlestick> Candles)
        {
            int limit = Candles.Count - 30;
            _trendNone = new decimal[Candles.Count];
            _trendRed = new decimal[Candles.Count];

            for (int i = 0; i < limit; i++)
            {
                _trendNone[i] = 2 * TrendMA(Candles, i, (int)Math.Round(_period / _filterNumber)) - TrendMA(Candles, i, (int)_period);
            }

            int maPeriod = (int)Math.Round(Math.Sqrt((double)_period));
            for (int i = 0; i < limit; i++)
            {
                // _trendRed[i] = iMAOnArray(_trendNone, 0, maPeriod, 0, ma_method, i);
                _trendRed[i] = MovingAverage.OnArray(_trendNone, 0, maPeriod, 0, MaMethod.LWMA, i);
            }
        }

        public decimal GetValue(int bar)
        {
            bar = Math.Max(0, bar);
            bar = Math.Min(bar, _trendRed.Length - 1);
            return _trendRed[bar];
        }
    }
}
