using CryptoTrading.App.Core;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoTrading.App.Monitor
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddTradeMonitor(this IServiceCollection services, System.Collections.Generic.Dictionary<string, IPosition> dictionaryPositions, ServiceProvider masterServices)
        {
            services.AddScoped<ITradeProcessor, TradeProcessor>();
            services.AddScoped<ITradeFactory, TestTradeFactory>();
            services.AddTransient<ITradeMonitor, TradeMonitor>(x=> new TradeMonitor(masterServices.GetService<IMarketMonitor>()));
            services.AddScoped<IMarketMonitorFactory, MarketMonitorFactory>(provider => new MarketMonitorFactory(provider));
            services.AddScoped<IPositions, TestPositions>(provider => new TestPositions(provider.GetService<ITradeFactory>(),dictionaryPositions));
            return services;
        }
    }
}
