using System.IO;
using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Extensions;
using CryptoTrading.App.MarketData;
using CryptoTrading.App.Process;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Calibration
{
    internal class Program
    {
        //Strategy Calibration 
        static void Main(string[] args)
        {
            var database = string.IsNullOrEmpty(args[0]) ? "SERVER=SQL-SERVER-2016" : args[0];
            var services = new ServiceCollection()
                .AddCalibrationService(database)
                .AddLogging(builder => builder // configure logging.
                    .SetMinimumLevel(LogLevel.Information)
                    .AddConsole()
                )
                .BuildServiceProvider();

            var process = services.GetService<IStrategyCalibration>();
            //load data from SQL, thread for each pair?

            //generate signals and add it to the data.

            //calculate the returns based off the signals 

            //Calculate probability of returns

            //change input variables?
        }

        public static ServiceProvider BuildServices(IConfig config)
        {
            var Configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", true, false)
                .AddUserSecrets<ProcessManagement>() // for access to API key and secret.
                .Build();

            var services = new ServiceCollection()
                .AddMarketData(config)
                .AddBinance()
                .AddTradingCore(config)
                .AddAlgorithm(config)
                .AddLogging(builder => builder // configure logging.
                    .SetMinimumLevel(LogLevel.Information)
                    .AddFile(config.FilePath, 10000, LogLevel.Information)
                    .AddConsole())
                .BuildServiceProvider();
            return services;
        }
    }
}
