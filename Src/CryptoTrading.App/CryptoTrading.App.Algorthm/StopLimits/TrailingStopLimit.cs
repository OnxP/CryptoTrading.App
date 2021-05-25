using Binance;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Algorthm.StopLimits
{
    class TrailingStopLimit : IStopLimitTracker
    {
        private decimal _risk = 3.48m / 100m;
        private decimal _increment = 1.52m / 100m;
        private int i = 1;
        private decimal _currentPrice;
        private decimal _boughtPrice;

        public TrailingStopLimit(decimal risk, decimal increment)
        {
            _risk = risk;
            _increment = increment;
        }

        public decimal StopLimitPrice { get; private set; }

        public decimal TargetPrice { get; private set; }
        public decimal CurrentPrice { get => _currentPrice; set => _currentPrice = value; }

        public void Configure(Order order)
        {
            //set stopLimitValue to 10% of current price.
            _currentPrice = order.Price;
            _boughtPrice = order.Price;
            StopLimitPrice = _currentPrice * (1 - _risk);
            TargetPrice = _currentPrice * (1 + _increment);

            //when the price is small, where a single sitoshi becomes more that 1% the risk and increments need to be adjusted.
            while (Math.Round(_currentPrice, 9) == Math.Round(TargetPrice, 9))
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
            TargetPrice += _boughtPrice * _increment;
            StopLimitPrice += _boughtPrice * _increment;
            if (i == 1) StopLimitPrice = TargetPrice * (1 - _increment);
            i++;

        }
    }
}
