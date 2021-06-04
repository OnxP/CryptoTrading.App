using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core.TradeRequest
{
    public interface ICancelRequest : IRequest
    {
        long ClientOrderId { get; set; }
    }
}
