using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.Message_Broker;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoTrading.App.Core
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddTradingCore(this IServiceCollection services)
        {
            services.AddSingleton<IMessageBroker, MessageBroker>();
            return services;
        }

        public static IServiceCollection AddKey(this IServiceCollection services, string Key)
        {
            services.AddScoped<IKey, Key>(x => new Key(Key));
            return services;
        }
    }
}
