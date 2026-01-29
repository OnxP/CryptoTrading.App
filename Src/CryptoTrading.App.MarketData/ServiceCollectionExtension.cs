using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.MarketMonitorFactory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        public static IServiceCollection AddMarketData(this IServiceCollection services,IConfig config)
        {
            switch (config.RunType)
            {
                case RunTypeEnum.BackTesting:
                    services.AddTransient<IMarketData, DbMarketData>(p=> new DbMarketData(p.GetService<ILogger<DbMarketData>>(),p.GetService<ICandleStickManagement>(),p.GetService<IDbData>(),config.From,config.To));
                    services.AddSingleton<ICandleStickManagement, DbCandleStickManagement>();
                    break;
                case RunTypeEnum.LiveTesting:
                case RunTypeEnum.Live:
                    services.AddTransient<IMarketData, LiveMarketData>();
                    break;
            }
            

            return services;
        }

        public static IServiceCollection AddMarketMonitor(this IServiceCollection services, IConfig config)
        {
            switch (config.RunType)
            {
                case RunTypeEnum.BackTesting:
                    services.AddSingleton<IMarketMonitor, DbMarketMonitor>();
                    break;
                case RunTypeEnum.LiveTesting:
                    services.AddTransient<IMarketMonitor, TestLiveMarketMonitor>();
                    break;
                case RunTypeEnum.Live:
                    services.AddTransient<IMarketMonitor, LiveMarketMonitor>();
                    break;
            }

            return services;
        }
    }
}
