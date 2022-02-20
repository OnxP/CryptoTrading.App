using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.Entity;
using System.Threading.Tasks;
using CryptoTrading.App.AlgorithmTesting;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Database.Config;
using CryptoTrading.App.MarketData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tulip;

namespace CryptoTrading.App.AlgorthmTesting
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

            var config = new CryptoConfig();
            config.RunType = RunTypeEnum.BackTesting;

            var services = new ServiceCollection()
                    .AddMarketData(config)
                    .AddMarketMonitor(config)
                    //.AddStoreTrades(RunTypeEnum.BackTesting)
                    .AddTradingCore(config)
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
