using System;
using System.Linq;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Exchange.BinanceNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CryptoTrading.App.Broker
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Register the broker subsystem. Picks <see cref="IMarket"/>
        /// implementation by <see cref="IConfig.RunType"/>:
        /// <list type="bullet">
        /// <item>BackTesting → <see cref="TestMarket"/> (in-memory, no network)</item>
        /// <item>LiveTesting → <see cref="TestLiveMarket"/> (real balances, synthetic fills)</item>
        /// <item>Live → <see cref="LiveMarket"/> (real exchange calls)</item>
        /// </list>
        /// For the two network-facing modes this also ensures an
        /// <see cref="IExchangeProvider"/> is registered via
        /// <see cref="ServiceCollectionExtensions.AddBinanceNetExchangeProvider"/>.
        /// Composition roots that already call that extension (or that
        /// wire a different provider) are left alone — we only add it if
        /// nothing else has.
        /// </summary>
        public static IServiceCollection AddBroker(this IServiceCollection services, IConfig config)
        {
            switch (config.RunType)
            {
                case RunTypeEnum.BackTesting:
                    services.AddTransient<IMarket, TestMarket>();
                    break;
                case RunTypeEnum.LiveTesting:
                    EnsureExchangeProvider(services, config);
                    services.AddTransient<IMarket, TestLiveMarket>();
                    break;
                case RunTypeEnum.Live:
                    EnsureExchangeProvider(services, config);
                    services.AddTransient<IMarket, LiveMarket>();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            services.AddTransient<IBroker, CryptoBroker>();

            return services;
        }

        public static IServiceCollection AddTestBroker(this IServiceCollection services)
        {
            services.AddTransient<IMarket, TestMarket>();
            services.AddTransient<IBroker, CryptoBroker>();
            return services;
        }

        /// <summary>
        /// Idempotently register an IExchangeProvider. Composition roots may
        /// call AddBinanceNetExchangeProvider themselves (e.g. to wire a
        /// different exchange or use a custom config); if they do, skip.
        /// </summary>
        private static void EnsureExchangeProvider(IServiceCollection services, IConfig config)
        {
            if (services.Any(s => s.ServiceType == typeof(IExchangeProvider)))
                return;
            services.AddBinanceNetExchangeProvider(config);
        }
    }
}
