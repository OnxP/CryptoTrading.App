using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Monitor.StopLimitTracker
{
    class TrailingStopLimit : IStopLimitTracker
    {
        private decimal _risk = 0.02m;
        private decimal _increment = 0.01m;
        private int i = 1;
        private decimal _currentPrice;
        private decimal _boughtPrice;
        public decimal StopLimitPrice { get; private set; }

        public decimal TargetPrice { get; private set; }

        public void Configure(Order order)
        {
            //set stopLimitValue to 10% of current price.
            _currentPrice = order.Price;
            _boughtPrice = order.Price;
            StopLimitPrice = _currentPrice * (1-_risk);
            TargetPrice = _currentPrice * (1+_increment);

            //when the price is small, where a single sitoshi becomes more that 1% the risk and increments need to be adjusted.
            while(Math.Round(_currentPrice,9) == Math.Round(TargetPrice,9))
            {
                _increment *= 2;
                _risk *= 2;

                StopLimitPrice = _currentPrice * (1 - _risk);
                TargetPrice = _currentPrice * (1 + _increment);
            }
        }

        public void Dispose()
        {
            return;
        }

        public void MoveStopLimit()
        {
            StopLimitPrice = _boughtPrice * (1 + _increment * i);
            TargetPrice = _boughtPrice * (1 + _increment * i);
            i++;
        }
    }
}
