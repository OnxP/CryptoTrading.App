using System;
using System.Linq;
using CryptoTrading.App.Broker.Account;
using CryptoTrading.App.Broker.Position;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;

using CryptoTrading.App.Exchange.BinanceNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Broker
{
    public static class ServiceCollectionExtensions
    {
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

            services.AddSingleton<IPositions>(provider =>
                new BrokerPositions(
                    provider.GetService<ILogger<BrokerPositions>>()));

            RegisterAccount(services, config);
            services.AddTransient<IBroker, CryptoBroker>();

            return services;
        }

        public static IServiceCollection AddTestBroker(this IServiceCollection services)
        {
            services.AddTransient<IMarket, TestMarket>();
            services.AddSingleton<IPositions>(provider =>
                new BrokerPositions(
                    provider.GetService<ILogger<BrokerPositions>>()));
            services.AddSingleton<IAccount>(provider =>
                new SpotAccount(
                    provider.GetService<IPositions>(),
                    provider.GetService<ILogger<SpotAccount>>()));
            services.AddTransient<IBroker, CryptoBroker>();
            return services;
        }

        private static void RegisterAccount(IServiceCollection services, IConfig config)
        {
            switch (config.TradingVenue)
            {
                case TradingVenue.Spot:
                    services.AddSingleton<IAccount>(provider =>
                        new SpotAccount(
                            provider.GetService<IPositions>(),
                            provider.GetService<ILogger<SpotAccount>>()));
                    break;
                case TradingVenue.Futures:
                    services.AddSingleton<IAccount>(provider =>
                        new FuturesAccount(
                            provider.GetService<IPositions>(),
                            provider.GetService<ILogger<FuturesAccount>>()));
                    break;
                case TradingVenue.Margin:
                    services.AddSingleton<IAccount>(provider =>
                        new MarginAccount(
                            provider.GetService<IPositions>(),
                            provider.GetService<ILogger<MarginAccount>>()));
                    break;
                default:
                    services.AddSingleton<IAccount>(provider =>
                        new SpotAccount(
                            provider.GetService<IPositions>(),
                            provider.GetService<ILogger<SpotAccount>>()));
                    break;
            }
        }

        private static void EnsureExchangeProvider(IServiceCollection services, IConfig config)
        {
            if (services.Any(s => s.ServiceType == typeof(IExchangeProvider)))
                return;
            services.AddBinanceNetExchangeProvider(config);
        }
    }
}
