using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoTrading.App.Persistence
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddCryptoDbPg(this IServiceCollection services, string connectionString)
        {
            services.AddDbContext<CryptoDbContextPg>(options =>
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.CommandTimeout(120)));

            return services;
        }
    }
}
