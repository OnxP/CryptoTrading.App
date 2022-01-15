using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Algorithm.CustomIndicators
{
    public enum AppliedPrice
    {
        Close,
        Open,
        High,
        Low
    }

    public enum MaMethod
    {
        Sma,
        Ema,
        SSMA,
        LWMA
    }

    // todo : MaMethod is not used atm...

    public static class MovingAverage
{
        public static decimal Get(List<Candlestick> Candles, int bar, int period, MaMethod method = MaMethod.Sma, AppliedPrice appliedPrice = AppliedPrice.Close)
        {
            decimal price = 0M;
            for (int i = 0; i < period; ++i)
            {
                int idx = Math.Min(bar + i, Candles.Count - 1);
                switch (appliedPrice)
                {
                    case AppliedPrice.Close:
                        price += Candles[idx].Close;
                        break;

                    case AppliedPrice.Open:
                        price += Candles[idx].Open;
                        break;

                    case AppliedPrice.High:
                        price += Candles[idx].High;
                        break;

                    case AppliedPrice.Low:
                        price += Candles[idx].Low;
                        break;
                }
            }
            return price / ((decimal)period);
        }

        // todo: total, ma_shift and maMethod are not used atm...
        public static decimal OnArray(decimal[] array, int total, int maPeriod, int ma_shift, MaMethod maMethod, int shift)
        {
            decimal price = 0M;
            for (int i = 0; i < maPeriod; ++i)
            {
                price += array[shift + i];
            }
            return price / ((decimal)maPeriod);
        }
    }
}
