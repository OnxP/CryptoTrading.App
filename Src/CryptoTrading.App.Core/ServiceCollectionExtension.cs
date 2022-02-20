using System;
using CryptoTrading.App.Core.BinanceAccount;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.Message_Broker;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoTrading.App.Core
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddTradingCore(this IServiceCollection services, IConfig config)
        {
            switch (config.RunType)
            {
                case RunTypeEnum.BackTesting:
                    services.AddSingleton<IMessageBroker, MessageBroker>();
                    services.AddSingleton<IDbData, DbData>();
                    break;
                case RunTypeEnum.LiveTesting:
                case RunTypeEnum.Live:
                    services.AddSingleton<IMessageBroker, MessageBroker>();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return services;
        }

        public static IServiceCollection AddKey(this IServiceCollection services, string Key)
        {
            services.AddScoped<IKey, Key>(x => new Key(Key));
            return services;
        }

        public static IServiceCollection AddAccountService(this IServiceCollection services, IConfig config)
        {
            switch (config.RunType)
            {
                case RunTypeEnum.BackTesting:
                    services.AddSingleton<IAccountConfig, DatabaseAccountConfig>(provider=> new DatabaseAccountConfig(config));
                    break;
                case RunTypeEnum.LiveTesting:
                    break;
                case RunTypeEnum.Live:
                    services.AddSingleton<IAccountConfig,BinanceAccountConfig>();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return services;
        }
    }
}
