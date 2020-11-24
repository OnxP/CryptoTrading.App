using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core.Trade
{
    public class MarketTransaction : Transaction
    {
        public override TransactionType Type => TransactionType.MarketTransaction;
        public override Order Order
        {
            get; set;
        }
    }
}
