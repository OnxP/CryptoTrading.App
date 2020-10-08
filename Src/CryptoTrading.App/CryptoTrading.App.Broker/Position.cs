using System;
using System.Collections.Generic;
using System.Text;
using Binance;

namespace CryptoTrading.App.Broker
{
    public class Position : IPosition
    {
        public bool CheckFunds(string sellAmount)
        {
            throw new NotImplementedException();
        }

        public bool HasOpenPosition { get; set; }
        public void UpdateOrder(Order order)
        {
            throw new NotImplementedException();
        }
    }
}
