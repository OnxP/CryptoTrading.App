using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Monitor.StopLimitTracker
{
    class FixedTrailingStopLimit : IStopLimitTracker
    {
        private decimal _risk = 0.0098m;
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
            StopLimitPrice = _boughtPrice * (1-_risk);
            TargetPrice = _boughtPrice * (1+(_risk + _increment));

            //when the price is small, where a single sitoshi becomes more that 1% the risk and increments need to be adjusted.
            while(Math.Round(_boughtPrice, 9) == Math.Round(TargetPrice,9))
            {
                _increment *= 2;
                _risk *= 2;

                StopLimitPrice = _boughtPrice * (1 - _risk);
                TargetPrice = _boughtPrice * (1 + _increment);
            }
        }

        public void Dispose()
        {
            return;
        }

        public void MoveStopLimit()
        {
                
            if (i == 2)
            {
                StopLimitPrice = CurrentPrice;
                TargetPrice += _boughtPrice * (_increment);
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
