using Binance;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Algorthm.StopLimits
{
    class FixedTrailingStopLimit : IStopLimitTracker
    {
        private decimal _risk = 0.991m;
        private decimal _fixed = 1.0152m;
        private decimal _increment = 0.009m;
        private int i = 2;
        private decimal _currentPrice;
        private decimal _boughtPrice;
        public decimal StopLimitPrice { get; set; }
        public FixedTrailingStopLimit(decimal risk, decimal increment)
        {
            _risk = 1 - (risk / 100m);
            _fixed = 1 + (increment / 100m);
        }
        public decimal TargetPrice { get; set; }
        public decimal CurrentPrice { get => _currentPrice; set { _currentPrice = value; } }
        public DateTime EndDateTime { get; set; }
        public bool IsOpen { get; set; }
        public decimal Increment { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public decimal Risk { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public void Configure(Order order)
        {
            //set stopLimitValue to 10% of current price.
            _boughtPrice = order.Price;
            IsOpen = true;
            //i = 1;
        }

        public void Dispose()
        {
            IsOpen = false;
            return;
        }

        public void MoveStopLimit()
        {
            if (i == 1)
            {
                _increment = (_currentPrice - _boughtPrice) / _boughtPrice;
                StopLimitPrice = _boughtPrice * 1.002m;
                TargetPrice += CurrentPrice * _increment;
            }
            else
            {
                TargetPrice += TargetPrice * _increment;
                StopLimitPrice += StopLimitPrice * _increment;
            }
            i++;
        }
    }
}
