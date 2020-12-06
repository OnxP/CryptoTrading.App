using CryptoTrading.App.Core.Trade;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core.MarketMonitorFactory
{
    public class MarketMonitorFactory
    {
        public MarketMonitorFactory()
        {

        }
        public ITradeMonitor CreateMonitor(ITrade trade)
        {
            throw new NotImplementedException();
        }
    }
}
