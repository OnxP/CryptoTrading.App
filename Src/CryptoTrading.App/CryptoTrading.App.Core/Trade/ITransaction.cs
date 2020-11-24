using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITransaction
    {
        TransactionType Type { get; }
        public string Pair { get; }

    }
}
