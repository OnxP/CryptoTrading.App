using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Database.Config;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoTrading.App.Process
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddCryptoService(this IServiceCollection services)
        {
            services.AddSingleton<IProcessManagement,ProcessManagement>();
            services.AddTransient<IProcess,CryptoProcess>(provider => new CryptoProcess(new CryptoDbConfigContext()));
            return services;
        }
    }
}
