using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Monitor.StopLimitTracker
{
    class FixedTrailingStopLimit : IStopLimitTracker
    {
        private decimal _risk = 0.991m;
        private decimal _fixed = 1.0152m;
        private decimal _increment = 0.005m;
        private int i = 1;
        private decimal _currentPrice;
        private decimal _boughtPrice;
        public decimal StopLimitPrice { get; private set; }

        public decimal TargetPrice { get; private set; }
        public decimal CurrentPrice { get => _currentPrice; set { _currentPrice = value; } }

        public void Configure(Order order)
        {
            //set stopLimitValue to 10% of current price.
            _boughtPrice = order.Price;
            StopLimitPrice = _boughtPrice * _risk;
            TargetPrice = _boughtPrice * _fixed;

            //when the price is small, where a single sitoshi becomes more that 1% the risk and increments need to be adjusted.
            while (Math.Round(_boughtPrice, 9) == Math.Round(TargetPrice,9))
            {
                _fixed += _increment;
                _risk += _increment;

                StopLimitPrice = _boughtPrice * _risk;
                TargetPrice = _boughtPrice * _fixed;
            }
        }

        public void Dispose()
        {
            return;
        }

        public void MoveStopLimit()
        {
                
            if (i == 1)
            {
                StopLimitPrice = CurrentPrice - (CurrentPrice * _increment);
                TargetPrice += CurrentPrice * (_increment);
            }
            else
            {
                TargetPrice += _boughtPrice * (_increment);
                StopLimitPrice += _boughtPrice * (_increment);
            }
            i++;            
        }
    }
}
