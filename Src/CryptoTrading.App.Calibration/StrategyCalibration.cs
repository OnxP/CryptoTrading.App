using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database.Config;
using CryptoTrading.App.Core.Extensions;
using CryptoTrading.App.MarketData;
using CryptoTrading.App.Process;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Calibration
{
    public class StrategyCalibration : IStrategyCalibration
    {
        public StrategyCalibration(CryptoDbConfigContext context,Func<IAlgorithm> aglo, IMarketData market)
        {
            Config = context.CryptoConfigs.First(x => x.Id == 1);
            Config.SetContext(context);
            MarketData = market;
            Algorithm = aglo;
        }

        private Func<IAlgorithm> Algorithm { get; }
        private IConfig Config { get; }
        private IMarketData MarketData { get; }

        public void WireMarketDataEvents(List<ExchangeSymbol> symbols)
        {
            var interval = Config.Interval;

            foreach (var symbol in symbols)
            {
                //need to create a unique instance of algo
                var algorithm = Algorithm.Invoke();
                algorithm.Configure(Config);
                MarketData.InitialDataLoadSubscribe(symbol.Ticker, interval, algorithm.ProcessHistoricMarketData);
                MarketData.InitialDataStreamSubscribe(symbol.Ticker, interval, algorithm.ProcessLiveCandleStick);
            }
        }
        public void RemoveMarketDataEvents(List<ExchangeSymbol> removeSymbols)
        {
            var interval = Config.Interval;
            foreach (var symbol in removeSymbols)
            {
                MarketData.InitialDataLoadUnSubscribe(symbol.Ticker, interval);
                MarketData.InitialDataStreamUnSubscribe(symbol.Ticker, interval);
            }
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

    public interface IStrategyCalibration
    {
    }
}
