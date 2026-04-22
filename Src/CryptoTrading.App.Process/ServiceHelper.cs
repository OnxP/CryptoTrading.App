using System.IO;
using Binance;
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
                // Fully qualified: both the bundled SDK and Binance.Net
                // expose AddBinance extensions, and Broker now transitively
                // surfaces the Binance.Net one. Explicit bool picks the
                // bundled overload, which is still required by MarketData /
                // Monitor / AccountService. They migrate in PR 5b.
                .AddBinance(useSingleCombinedStream: false)
                .AddTradingCore(config)
                .AddTradeMonitor(config)
                .AddBroker(config)
                //.AddAlgorithm(config)
                //.AddRegimeBasedAlgorithm()
                .AddHtfRsiVolExpansionAlgorithm()
                .AddMarketMonitor(config)
                .AddAccountService(config)
                .AddLogging(builder => builder // configure logging.
                    .SetMinimumLevel(LogLevel.Information)
                    .AddFile(config.FilePath, 10000, LogLevel.Information)
                    .AddConsole())
                .BuildServiceProvider();
            return services;
        }
    }
}
