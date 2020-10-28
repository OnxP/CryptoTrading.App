using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core.Trade
{
    public class Trade : ITrade
    {
        public decimal Price { get; set; }
        public string Symbol { get; set; }
        public OrderSide OrderType { get; set; }
        public decimal Quantity { get; set; }
        public decimal Fee => Price * Quantity;
    }
}
