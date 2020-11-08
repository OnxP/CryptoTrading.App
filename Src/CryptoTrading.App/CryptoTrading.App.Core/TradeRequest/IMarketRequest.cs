using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core.TradeRequest
{
    public interface IMarketRequest : IRequest
    {
        OrderSide? OrderType { get; set; }
        decimal Quantity { get; set; }
        decimal Price { get; set; }
    }
}
