using Binance;
using CryptoTrading.App.Algorithm;
using CryptoTrading.App.Core;
using CryptoTrading.App.Broker;
using CryptoTrading.App.Core.Extensions;
using CryptoTrading.App.MarketData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using CryptoTrading.App.Monitor;
using CryptoTrading.App.Core.Position;
using System.Threading;
using CryptoTrading.App.Core.Trade;
using System.Text;
using System.Linq;
using Tulip;
using CryptoTrading.App.Core.Database.Indicators;
using CryptoTrading.App.Core.Database.StoreTrades;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Database;
using System.Data.Common;
using System.Data.Entity;

namespace CryptoTrading.App.AlgorithmTesting
{
    class Program
    {
        
        static void Main(string[] args)
        {
            DbProviderFactories.RegisterFactory("System.Data.SqlClient", System.Data.SqlClient.SqlClientFactory.Instance);
            Database.SetInitializer<CryptoDbContext>(null);
            var customText = "MACD 1hr SRSI";
            var noOfTrades = new List<double>() { 4};
            var risks = new List<decimal>() { 2m };//,0.98m,1.98m };
            var increments = new List<decimal>() { 1.5m };//,2m,3m};
            var tasks = new List<Task>();

            var services = new ServiceCollection()
                    .AddDbMarketData()
                    .AddDbMarketMonitor(RunTypeEnum.BackTesting)
                    //.AddStoreTrades(RunTypeEnum.BackTesting)
                    .AddTradingCore()
                    .AddLogging(builder => builder // configure logging.
                        .SetMinimumLevel(LogLevel.Trace)
                        .AddConsole()
                        )
                    .BuildServiceProvider();

            var marketData = services.GetService<IMarketData>();
            var marketMonitor = services.GetService<IMarketMonitor>();
            //var TradeStorage = services.GetService<>(ITradeStorage);
            marketData.Configure(null);
            marketData.From = new DateTime(2021, 05, 01, 00, 00, 00);
            marketData.To = new DateTime(2021, 06, 01, 00, 00, 00);

            //foreach (var strat in strats)
            //{
                foreach (var noOfTrade in noOfTrades)
                {
                    foreach (var risk in risks)
                    {
                        foreach (var increment in increments)
                        {
                            tasks.Add(CreateTask(Indicators.gema, noOfTrade, risk, increment,marketData, customText, services));
                            
                        }
                    }
                }
            //}
            marketData.StartStream();
            foreach (var task in tasks)
            {
                task.RunSynchronously();
                task.Wait();
            }

            Task.WaitAll(tasks.ToArray());
            Console.WriteLine("Complete");
        }

        private static Task CreateTask(Indicator strat, double noOfTrade, decimal risk, decimal increment,IMarketData marketData,string text, ServiceProvider services)
        {
            var run = new RunContext();
            run.stratgy = strat;
            run.NoOfTrades = noOfTrade;
            run.Risk = risk;
            run.Increment = increment;
            run.marketData = marketData;
            run.CustomText = text;
            run.RunApp(services);
            return new Task(run.CompleteApp);
        }
    }
}
