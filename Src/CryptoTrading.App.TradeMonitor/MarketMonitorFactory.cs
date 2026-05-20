using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace CryptoTrading.App.Monitor
{
    public class MarketMonitorFactory : IMarketMonitorFactory
    {
        IServiceProvider _services;
        public MarketMonitorFactory(IServiceProvider provider)
        {
            _services = provider;
        }

        public async Task<ITradeMonitor> CreateMonitor(ITradeSignal signal)
        {
            ITradeMonitor monitor = _services.GetService<ITradeMonitor>();
            monitor.AcceptSignal(signal);
            await monitor.SubscribetToMarketData();
            return monitor;
        }
    }
}
