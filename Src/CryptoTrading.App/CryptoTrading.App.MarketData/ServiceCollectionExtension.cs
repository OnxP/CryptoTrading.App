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
    }
}
