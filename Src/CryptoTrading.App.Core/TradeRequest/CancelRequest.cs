using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core.TradeRequest
{
    public class CancelRequest : ICancelRequest
    {
        public CancelRequest(long clientOrderId, string symbol)
        {
            Symbol = symbol;
            ClientOrderId = clientOrderId;
        }

        public long ClientOrderId { get; set; }

        public string Symbol { get; set; }
    }
}
