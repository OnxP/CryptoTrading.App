using System.IO;
using Binance;
using CryptoTrading.App.Algorithm;
using CryptoTrading.App.Algorithm.DualRegime.Persistence;
using CryptoTrading.App.Broker;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Extensions;
using CryptoTrading.App.GrpcClients;
using CryptoTrading.App.MarketData;
using CryptoTrading.App.Monitor;
using CryptoTrading.App.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Process
{
    public static class ServiceHelper
    {
        public static ServiceProvider BuildServices(IConfig config)
        {
            var Configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", true, false)
                .AddUserSecrets<ProcessManagement>() // for access to API key and secret.
                .Build();

            var servicesSection = Configuration.GetSection("Services");
            var useGrpc = !string.IsNullOrEmpty(servicesSection["MarketDataEndpoint"]);

            var sc = new ServiceCollection();

            if (useGrpc)
            {
                var marketDataEndpoint = servicesSection["MarketDataEndpoint"];
                var brokerEndpoint = servicesSection["BrokerEndpoint"];
                var exchangeId = servicesSection["ExchangeId"] ?? "Binance";
                var venueStr = servicesSection["Venue"] ?? "Spot";
                var venue = System.Enum.TryParse<TradingVenue>(venueStr, true, out var v) ? v : TradingVenue.Spot;

                sc.AddGrpcClients(marketDataEndpoint, brokerEndpoint, exchangeId, venue);
            }
            else
            {
                sc.AddBinance(useSingleCombinedStream: false);
            }

            var connString = Configuration.GetConnectionString("CryptoDb");
            if (!string.IsNullOrEmpty(connString))
            {
                sc.AddCryptoDbPg(connString);
                sc.AddTransient<AlgoStatePersistence>();
            }

            sc.AddMarketData(config)
              .AddTradingCore(config)
              .AddTradeMonitor(config)
              .AddBroker(config)
              .AddDualRegimeAlgorithm()
              .AddMarketMonitor(config)
              .AddAccountService(config)
              .AddLogging(builder => builder
                  .SetMinimumLevel(LogLevel.Information)
                  .AddFile(config.FilePath, 10000, LogLevel.Information)
                  .AddConsole());

            return sc.BuildServiceProvider();
        }
    }
}
