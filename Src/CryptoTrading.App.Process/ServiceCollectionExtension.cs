using System.Data.Common;
using System.Data.Entity;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Database.Config;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoTrading.App.Process
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddCryptoService(this IServiceCollection services,string databaseName)
        {
            DbProviderFactories.RegisterFactory("System.Data.SqlClient", System.Data.SqlClient.SqlClientFactory.Instance);
            Database.SetInitializer<CryptoDbContext>(null);
            services.AddSingleton<IProcessManagement,ProcessManagement>();
            services.AddTransient<IProcess,CryptoProcess>(provider => new CryptoProcess(new CryptoDbConfigContext(databaseName)));
            return services;
        }
    }
}
