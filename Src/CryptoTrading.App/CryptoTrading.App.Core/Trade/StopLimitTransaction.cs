using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core.Trade
{
    public class StopLimitTransaction : Transaction
    {
        public override TransactionType Type => TransactionType.StopLimitTransaction;
    }
}
