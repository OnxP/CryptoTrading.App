using System;
using System.Collections.Generic;
using CryptoTrading.App.Broker.Position;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.MarketMonitorFactory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Monitor
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddTradeMonitor(this IServiceCollection services, Dictionary<string, IPosition> dictionaryPositions, ServiceProvider masterServices)
        {
            services.AddScoped<ITradeProcessor, TradeProcessor>();
            services.AddTransient<ITradeMonitor, TradeMonitor>(x => new TradeMonitor(
                masterServices.GetService<ILogger<TradeMonitor>>(),
                masterServices.GetService<IMarketMonitor>(),
                null,
                masterServices.GetService<IBroker>()));
            services.AddScoped<IMarketMonitorFactory, MarketMonitorFactory>(provider => new MarketMonitorFactory(provider));
            services.AddScoped<IPositions, BrokerPositions>(provider => new BrokerPositions(provider.GetService<ILogger<BrokerPositions>>(), dictionaryPositions));
            return services;
        }

        public static IServiceCollection AddTradeMonitor(this IServiceCollection services, IConfig config)
        {
            switch (config.RunType)
            {
                case RunTypeEnum.BackTesting:
                    services.AddTransient<IPositions, BrokerPositions>(provider => new BrokerPositions(provider.GetService<ILogger<BrokerPositions>>()));
                    break;
                case RunTypeEnum.LiveTesting:
                case RunTypeEnum.Live:
                    services.AddTransient<IPositions, BrokerPositions>();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            services.AddTransient<ITradeProcessor, TradeProcessor>();
            services.AddTransient<ITradeMonitor, TradeMonitor>(x => new TradeMonitor(
                x.GetService<ILogger<TradeMonitor>>(),
                x.GetService<IMarketMonitor>(),
                config,
                x.GetService<IBroker>()));
            services.AddTransient<IMarketMonitorFactory, MarketMonitorFactory>(provider => new MarketMonitorFactory(provider));
            return services;
        }
    }
}
