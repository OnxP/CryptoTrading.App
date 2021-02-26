using CryptoTrading.App.Core;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Monitor.StopLimitTracker;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoTrading.App.Monitor
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddTradeMonitor(this IServiceCollection services, RunTypeEnum runType, System.Collections.Generic.Dictionary<string, IPosition> dictionaryPositions)
        {
            switch (runType)
            {
                case RunTypeEnum.BackTesting:
                    services.AddTransient<IMarketMonitor, DbMarketMonitor>();
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
            services.AddSingleton<ITradeProcessor, TradeProcessor>();
            services.AddSingleton<ITradeFactory, TestTradeFactory>();
            services.AddTransient<ITradeMonitor, TradeMonitor>();
            services.AddSingleton<IMarketMonitorFactory, MarketMonitorFactory>(provider => new MarketMonitorFactory(provider));
            services.AddTransient<IStopLimitTracker, TrailingStopLimit>();
            services.AddSingleton<IPositions, TestPositions>(provider => new TestPositions(provider.GetService<ITradeFactory>(),dictionaryPositions));



            return services;
        }
    }
}
