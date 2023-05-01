using CryptoTrading.App.Algorithm.StopLimits;
using CryptoTrading.App.Algorithm.TradingStrategies;
using CryptoTrading.App.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.Entity;
using System.Linq;
using CryptoTrading.App.Algorithm;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Database.Config;
using CryptoTrading.App.Process;

namespace CryptoTrading.App.Calibration
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAlgorithm(this IServiceCollection services)
        {
            services.AddTransient<ITradingStrategy, SimpleMacdTradingStrategy>();

            services.AddTransient<IAlgorithm, SimpleAlgorithm>();
            services.AddComposite<ITradingStrategy, CompositeTradingStrategy>();
            services.AddTransient<IStopLimitTracker, FixedProfitStopLimit>();

            return services;
        }

        public static IServiceCollection AddCryptoService(this IServiceCollection services, string databaseName)
        {
            DbProviderFactories.RegisterFactory("System.Data.SqlClient", System.Data.SqlClient.SqlClientFactory.Instance);
            Database.SetInitializer<CryptoDbContext>(null);
            services.AddSingleton<IProcessManagement, ProcessManagement>();
            services.AddTransient<IProcess, CryptoProcess>(provider => new CryptoProcess(new CryptoDbConfigContext(databaseName)));
            return services;
        }
        public static IServiceCollection AddAlgorithm(this IServiceCollection services, IConfig config)
        {
            services.AddTransient<ITradingStrategy, SuperTrendEMATradingStrategy>(provider => new SuperTrendEMATradingStrategy(provider.GetService<ILogger<TradingStrategy>>()));

            services.AddTransient<IAlgorithm, SimpleAlgorithm>();
            services.AddComposite<ITradingStrategy, CompositeTradingStrategy>();
            services.AddTransient<IStopLimitTracker, FixedProfitStopLimit>(provider => new FixedProfitStopLimit(Convert.ToDecimal(config.Risk), Convert.ToDecimal(config.Increment)));

            return services;
        }

        public static IServiceCollection AddCalibrationService(this IServiceCollection services, string databaseName)
        {
            DbProviderFactories.RegisterFactory("System.Data.SqlClient", System.Data.SqlClient.SqlClientFactory.Instance);
            Database.SetInitializer<CryptoDbContext>(null);
            services.AddTransient<IStrategyCalibration, StrategyCalibration>(provider => new StrategyCalibration(new CryptoDbConfigContext(databaseName),provider.GetService<IAlgorithm>,provider.GetService<IMarketData>()));
            return services;
        }

        public static void AddComposite<TInterface, TConcrete>(this IServiceCollection services)
  where TInterface : class
  where TConcrete : class, TInterface
        {
            var wrappedDescriptors = services.Where(s => s.ServiceType == typeof(TInterface)).ToList();
            foreach (var descriptor in wrappedDescriptors)
                services.Remove(descriptor);

            var objectFactory = ActivatorUtilities.CreateFactory(
              typeof(TConcrete),
              new[] { typeof(IEnumerable<TInterface>)});

            services.Add(ServiceDescriptor.Describe(
              typeof(TInterface),
              s => (TInterface)objectFactory(s, new[] { wrappedDescriptors.Select(d => s.CreateInstance(d)).Cast<TInterface>() }),
              wrappedDescriptors.Select(d => d.Lifetime).Max())
            );
        }

        public static object CreateInstance(this IServiceProvider services, ServiceDescriptor descriptor)
        {
            if (descriptor.ImplementationInstance != null)
                return descriptor.ImplementationInstance;

            if (descriptor.ImplementationFactory != null)
                return descriptor.ImplementationFactory(services);

            return ActivatorUtilities.GetServiceOrCreateInstance(services, descriptor.ImplementationType);
        }
    }
}
