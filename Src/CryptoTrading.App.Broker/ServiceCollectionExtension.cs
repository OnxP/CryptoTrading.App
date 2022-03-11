using System;
using CryptoTrading.App.Core;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoTrading.App.Broker
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddBroker(this IServiceCollection services,IConfig config)
        {
            switch (config.RunType)
            {
                case RunTypeEnum.BackTesting:
                    services.AddTransient<IMarket, TestMarket>();
                    break;
                case RunTypeEnum.LiveTesting:
                    services.AddTransient<IMarket, TestLiveMarket>();
                    break;
                case RunTypeEnum.Live:
                    services.AddTransient<IMarket, LiveMarket>();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            services.AddTransient<IBroker, CryptoBroker>();
            return services;
        }

        public static IServiceCollection AddTestBroker(this IServiceCollection services)
        {
            services.AddTransient<IMarket, TestMarket>();
            services.AddTransient<IBroker, CryptoBroker>();
            return services;
        }
    }
}
