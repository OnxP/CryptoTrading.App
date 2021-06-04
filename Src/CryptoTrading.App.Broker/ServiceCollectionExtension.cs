using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoTrading.App.Broker
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddTestBroker(this IServiceCollection services)
        {

            services.AddTransient<IMarket, TestMarket>();

            services.AddTransient<IBroker, CryptoBroker>();


            return services;
        }
    }
}
