using System.IO;
using CryptoTrading.App.Algorithm;
using CryptoTrading.App.Broker;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Extensions;
using CryptoTrading.App.MarketData;
using CryptoTrading.App.Monitor;
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

            var services = new ServiceCollection()
                .AddMarketData(config)
                .AddTradingCore(config)
                .AddTradeMonitor(config)
                .AddTestBroker(config)
                .AddAlgorithm(config)
                .AddMarketMonitor(config)
                .AddAccountService(config)
                .AddLogging(builder => builder // configure logging.
                    .SetMinimumLevel(LogLevel.Trace)
                    .AddFile(config.FilePath, LogLevel.Information)
                    .AddConsole())
                .BuildServiceProvider();
            return services;
        }
    }
}
