using Binance;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Algorthm.StopLimits
{
    class ManualStopLimit : IStopLimitTracker
    {
        private decimal _risk = 3.48m / 100m;
        private decimal _increment = 1.52m / 100m;
        private int i = 1;
        private decimal _currentPrice;
        private decimal _boughtPrice;

        public ManualStopLimit(decimal risk, decimal increment)
        {
            _risk = risk / 100m;
            _increment = increment / 100m;
        }

        public decimal StopLimitPrice { get; set; }

        public decimal TargetPrice { get; set; }
        public decimal CurrentPrice { get => _currentPrice; set => _currentPrice = value; }
        public DateTime EndDateTime { get; set; }
        public bool IsOpen { get; set; }
        public decimal Increment { get =>_increment; set => _increment = value; }
        public decimal Risk { get => _risk; set => _risk = value; }

        public void Configure(Order order)
        {
            IsOpen = true;
        }

        public void Dispose()
        {
            return;
        }

        public void MoveStopLimit()
        {

        }
    }
}
