using Binance;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.MarketMonitorFactory
{
    public class MarketMonitorFactory : IMarketMonitorFactory
    {
        IServiceProvider _services;
        public MarketMonitorFactory(IServiceProvider provider)
        {
            _services = provider;
        }

        public async Task<ITradeMonitor> CreateMonitor(ITradeRequest request, IPositions positions)
        {
            ITradeMonitor monitor = _services.GetService<ITradeMonitor>();
            monitor.AddRequest(request, positions);
            await monitor.SubscribetToMarketData();
            return monitor;
        }
    }
}
