using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core.TradeRequest
{
    public interface IStopLimitRequest : IRequest
    {
        OrderSide? OrderType { get; set; }
        decimal StopPrice { get; set; }
        decimal Quantity { get; set; }
    }
}
