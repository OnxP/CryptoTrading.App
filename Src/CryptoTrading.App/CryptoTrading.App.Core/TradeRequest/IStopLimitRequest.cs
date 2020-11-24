using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core.TradeRequest
{
    public interface IStopLimitRequest : IRequest
    {
        OrderSide? OrderType { get; }
        decimal StopPrice { get; }
        decimal Quantity { get; }
    }
}
