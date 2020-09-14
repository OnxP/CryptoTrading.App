using CryptoTrading.App.Core;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

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
    }
}
