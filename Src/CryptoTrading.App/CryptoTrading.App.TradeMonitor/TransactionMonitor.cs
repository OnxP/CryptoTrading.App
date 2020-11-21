using Binance;
using CryptoTrading.App.Monitor;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.TradeMonitor
{
    class TransactionMonitor : ITransactionMonitor
    {
        public bool Live => throw new NotImplementedException();

        public string Symbol => throw new NotImplementedException();

        public void Cancel(string order)
        {
            throw new NotImplementedException();
        }

        public void Update(Order order)
        {
            throw new NotImplementedException();
        }
    }
}
