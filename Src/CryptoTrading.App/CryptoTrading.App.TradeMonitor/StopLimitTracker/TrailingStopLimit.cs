using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Monitor.StopLimitTracker
{
    class TrailingStopLimit : IStopLimitTracker
    {
        private const decimal _risk = 0.02m;
        private const decimal _increment = 0.02m;
        private decimal _currentPrice;
        public decimal StopLimitPrice { get; private set; }

        public decimal TargetPrice { get; private set; }

        public void Configure(Order order)
        {
            //set stopLimitValue to 10% of current price.
            _currentPrice = order.Price;
            StopLimitPrice = _currentPrice * (1-_risk);
            TargetPrice = _currentPrice * (1+_increment);
        }

        public void MoveStopLimit()
        {
            StopLimitPrice = StopLimitPrice * (1+_increment);
            TargetPrice = TargetPrice * (1+_increment);
    }
}
