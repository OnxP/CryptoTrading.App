using System;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Monitor
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddTradeMonitor(this IServiceCollection services, System.Collections.Generic.Dictionary<string, IPosition> dictionaryPositions, ServiceProvider masterServices)
        {
            services.AddScoped<ITradeProcessor, TradeProcessor>();
            services.AddScoped<ITradeFactory, TradeFactory>();
            services.AddTransient<ITradeMonitor, TradeMonitor>(x=> new TradeMonitor(masterServices.GetService<ILogger<TradeMonitor>>(),masterServices.GetService<IMarketMonitor>(),null));
            services.AddScoped<IMarketMonitorFactory, MarketMonitorFactory>(provider => new MarketMonitorFactory(provider));
            services.AddScoped<IPositions, Positions>(provider => new Positions(provider.GetService<ILogger<Positions>>() ,provider.GetService<ITradeFactory>(),dictionaryPositions));
            return services;
        }

        public static IServiceCollection AddTradeMonitor(this IServiceCollection services, IConfig config)
        {
            switch (config.RunType)
            {
                case RunTypeEnum.BackTesting:
                    services.AddTransient<ITradeFactory, TradeFactory>();
                    services.AddTransient<IPositions, Positions>(provider => new Positions(provider.GetService<ILogger<Positions>>(), provider.GetService<ITradeFactory>()));
                    break;
                case RunTypeEnum.LiveTesting:
                case RunTypeEnum.Live:
                    services.AddTransient<ITradeFactory, TradeFactory>();
                    services.AddTransient<IPositions, Positions>();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            services.AddTransient<ITradeProcessor, TradeProcessor>();
            services.AddTransient<ITradeMonitor, TradeMonitor>(x=> new TradeMonitor(x.GetService<ILogger<TradeMonitor>>(), x.GetService<IMarketMonitor>(),config));
            services.AddTransient<IMarketMonitorFactory, MarketMonitorFactory>(provider => new MarketMonitorFactory(provider));
            return services;

            
            return services;
        }
    }
}
