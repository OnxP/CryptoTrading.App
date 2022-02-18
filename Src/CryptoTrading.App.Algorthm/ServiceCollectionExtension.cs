using CryptoTrading.App.Algorithm.StopLimits;
using CryptoTrading.App.Algorithm.TradingStrategies;
using CryptoTrading.App.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace CryptoTrading.App.Algorithm
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


        public static IServiceCollection AddAlgorithm(this IServiceCollection services, IConfig config)
        {
            services.AddTransient<ITradingStrategy, MacdRSITradingStrategy>(provider =>
                new MacdRSITradingStrategy(provider.GetService<ILogger<TradingStrategy>>(), config.NoOfTrades));

            services.AddTransient<IAlgorithm, SimpleAlgorithm>();
            services.AddComposite<ITradingStrategy, CompositeTradingStrategy>();
            services.AddTransient<IStopLimitTracker, ManualTrailingStopLimit>(provider => new ManualTrailingStopLimit(config.Risk, config.Increment));

            return services;
        }


        public static IServiceCollection AddAlgorithm(this IServiceCollection services, double NoOfTrades, decimal Risk, decimal Increment)
        {
            services.AddTransient<ITradingStrategy, MacdRSITradingStrategy>( provider => 
            new MacdRSITradingStrategy(provider.GetService<ILogger<TradingStrategy>>(), NoOfTrades));

            services.AddTransient<IAlgorithm, SimpleAlgorithm>();
            services.AddComposite<ITradingStrategy, CompositeTradingStrategy>();
            services.AddTransient<IStopLimitTracker, ManualTrailingStopLimit>(provider=> new ManualTrailingStopLimit(Risk,Increment));

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
