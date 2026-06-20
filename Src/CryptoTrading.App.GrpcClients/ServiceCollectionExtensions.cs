using System;
using CryptoTrading.App.Core.Exchange;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using BrokerClient = CryptoTrading.App.ServiceContracts.BrokerService.BrokerServiceClient;
using MarketDataClient = CryptoTrading.App.ServiceContracts.MarketDataService.MarketDataServiceClient;

namespace CryptoTrading.App.GrpcClients
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddGrpcClients(
            this IServiceCollection services,
            string marketDataEndpoint,
            string brokerEndpoint,
            string exchangeId,
            TradingVenue venue)
        {
            services.AddSingleton(_ =>
                GrpcChannel.ForAddress(marketDataEndpoint));

            services.AddSingleton(sp =>
            {
                var channel = sp.GetRequiredService<GrpcChannel>();
                return new MarketDataClient(channel);
            });

            services.AddSingleton(_ =>
            {
                var brokerChannel = GrpcChannel.ForAddress(brokerEndpoint);
                return new BrokerClient(brokerChannel);
            });

            services.AddSingleton<IExchangeProvider>(sp =>
            {
                var broker = sp.GetRequiredService<BrokerClient>();
                var marketData = sp.GetRequiredService<MarketDataClient>();
                return new GrpcExchangeProvider(broker, marketData, exchangeId, venue);
            });

            return services;
        }
    }
}
