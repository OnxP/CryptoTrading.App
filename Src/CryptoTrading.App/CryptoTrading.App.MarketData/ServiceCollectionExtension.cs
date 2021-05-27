using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoTrading.App.MarketData
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMarketData(this IServiceCollection services, bool liveStream = false)
        {
            if (liveStream)
            {
                services.AddTransient<IMarketData, LiveMarketData>();
            }
            else
            {
                services.AddTransient<IMarketData, HistoricalMarketData>();
            }

            return services;
        }

        public static IServiceCollection AddHistoricMarketData(this IServiceCollection services)
        {
            services.AddTransient<IMarketData, HistoricalMarketData>();
            
            return services;
        }
        public static IServiceCollection AddDbMarketData(this IServiceCollection services)
        {
            services.AddTransient<IMarketData, DbMarketData>();
            services.AddSingleton<ICandleStickManagement, DbCandleStickManagement>();

            return services;
        }

        public static IServiceCollection AddDbMarketMonitor(this IServiceCollection services, RunTypeEnum runType)
        {
            switch (runType)
            {
                case RunTypeEnum.BackTesting:
                    services.AddSingleton<IMarketMonitor, DbMarketMonitor>();
                    break;
                case RunTypeEnum.LiveTesting:
                    //services.AddTransient<IMarketMonitor, LiveTestMarketMonitor>();
                    break;
                case RunTypeEnum.Live:
                    //services.AddTransient<IMarketMonitor, LiveMarketMonitor>();
                    break;
                default:
                    break;
            }

            return services;
        }
    }
}
