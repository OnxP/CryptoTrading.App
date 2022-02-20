using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace CryptoTrading.App.Core.MarketMonitorFactory
{
    public class MarketMonitorFactory : IMarketMonitorFactory
    {
        IServiceProvider _services;
        public MarketMonitorFactory(IServiceProvider provider)
        {
            _services = provider;
        }
        public ITradeMonitor CreateMonitor(ITrade trade)
        {
            ITradeMonitor monitor = _services.GetService<ITradeMonitor>();
            monitor.AddTrade(trade);
            return monitor;
        }
    }
}
