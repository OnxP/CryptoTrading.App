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
    }
}
