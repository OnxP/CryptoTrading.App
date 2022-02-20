using System;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Monitor;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoTrading.App.Process
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddCryptoService(this IServiceCollection services)
        {
            services.AddScoped<IC, TradeProcessor>();
            services.AddScoped<ITradeFactory, TestTradeFactory>();
            return services;
        }
    }
}
