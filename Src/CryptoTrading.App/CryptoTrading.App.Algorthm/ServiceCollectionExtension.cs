using Binance;
using CryptoTrading.App.Algorthm.TradingStrategies;
using CryptoTrading.App.Core;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CryptoTrading.App.Algorthm
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAlgorthm(this IServiceCollection services)
        {
            services.AddTransient<ITradingStrategy, EmaTradingStrategy>();

            services.AddComposite<IAlgorthm, Algorthm>();

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
              new[] { typeof(IEnumerable<TInterface>) });

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
