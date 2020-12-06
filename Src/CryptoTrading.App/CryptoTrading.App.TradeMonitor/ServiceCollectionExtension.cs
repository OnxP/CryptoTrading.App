using CryptoTrading.App.Core;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Monitor.StopLimitTracker;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoTrading.App.Monitor
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddTradeMonitor(this IServiceCollection services, RunTypeEnum runType)
        {
            switch (runType)
            {
                case RunTypeEnum.BackTesting:
                    services.AddTransient<IMarketMonitor, TestMarketMonitor>();
                    break;
                case RunTypeEnum.LiveTesting:
                    services.AddTransient<IMarketMonitor, LiveTestMarketMonitor>();
                    break;
                case RunTypeEnum.Live:
                    services.AddTransient<IMarketMonitor, LiveMarketMonitor>();
                    break;
                default:
                    break;
            }

            services.AddTransient<ITradeMonitor, TradeMonitor>();
            services.AddTransient<IStopLimitTracker, TrailingStopLimit>();

            return services;
        }
    }
}
