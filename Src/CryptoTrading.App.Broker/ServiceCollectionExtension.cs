using System;
using Binance;
using CryptoTrading.App.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
                    //services.AddTransient<IMarket, TestLiveMarket>(provider=> new TestLiveMarket(provider.GetService<ILogger<TestLiveMarket>>(),provider.GetService<IBinanceApi>(),provider.GetService<IBinanceApiUserProvider>()?.CreateUser(config.ApiKey,config.ApiKeySecret)));
                    services.AddTransient<IMarket, TestMarket>();
                    break;
                case RunTypeEnum.Live:
                    services.AddTransient<IMarket, LiveMarket>(provider => new LiveMarket(provider.GetService<ILogger<LiveMarket>>(), provider.GetService<IBinanceApi>(), provider.GetService<IBinanceApiUserProvider>()?.CreateUser(config.ApiKey, config.ApiKeySecret)));
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
