using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoTrading.App.Monitor
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddTradeMonitor(this IServiceCollection services, RunTypeEnum runType, System.Collections.Generic.Dictionary<string, IPosition> dictionaryPositions, ServiceProvider masterServices)
        {
            switch (runType)
            {
                case RunTypeEnum.BackTesting:
                    services.AddTransient<IMarketMonitor, DbMarketMonitor>(x=>new DbMarketMonitor(masterServices.GetService< ICandleStickManagement>()));
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
            services.AddScoped<ITradeProcessor, TradeProcessor>();
            services.AddScoped<ITradeFactory, TestTradeFactory>();
            services.AddTransient<ITradeMonitor, TradeMonitor>();
            services.AddScoped<IMarketMonitorFactory, MarketMonitorFactory>(provider => new MarketMonitorFactory(provider));
            services.AddScoped<IPositions, TestPositions>(provider => new TestPositions(provider.GetService<ITradeFactory>(),dictionaryPositions));
            return services;
        }
    }
}
