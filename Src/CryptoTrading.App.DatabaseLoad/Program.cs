using System;
using System.IO;
using Binance;
using Binance.Application;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core;
using CryptoTrading.App.MarketData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Binance.Client;

namespace CryptoTrading.App.DatabaseLoad
{
    class Program
    {
        static void Main(string[] args)
        {
            var Configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", true, false)
                .AddUserSecrets<Program>() // for access to API key and secret.
                .Build();

            // Configure services.
            var ServiceProvider = new ServiceCollection()
                .AddBinance(useSingleCombinedStream: true)
                .AddHistoricMarketData()
                // Configure logging.
                .AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace)
                    .AddFile(Configuration.GetSection("Logging:File")))

                .BuildServiceProvider();
            //context.Database.ExecuteSqlCommand("TRUNCATE TABLE CandleStickDbs");
            IMarketData marketDate = ServiceProvider.GetService<IMarketData>();
            marketDate.Configure(null);
            marketDate.From = new DateTime(2020, 12, 01);//25-12-20
            var symbols = Symbol.BtcPairs;
            List<CandlestickInterval> intervals = new List<CandlestickInterval>()
            {
              CandlestickInterval.Minute
            , CandlestickInterval.Minutes_15
            };
        
            //subscribe to several symbols
            AddEvents(marketDate as AbstractMarketData, symbols,intervals);

            marketDate.StartStream();
            context.Dispose();

        }

        private static void AddEvents(AbstractMarketData marketDate, List<Symbol> symbols, List<CandlestickInterval> intervals)
        {
            foreach (var symbol in symbols)
            {
                foreach (var interval in intervals)
                {
                    marketDate.InitialDataLoadSubscribe(symbol, interval, SaveHistoricCandleStick);
                    marketDate.InitialDataStreamSubscribe(symbol, interval, AddCandleStick);
                }
            }
        }
        public static CryptoDBContext context = new CryptoDBContext();
        private static void AddCandleStick(CandlestickEventArgs obj)
        {
            lock (_object)
            {
                context.CandleSticks.Add(new CandleStickDb(obj.Candlestick));
                Check();
            }
        }
        public static int i = 0;
        private static void Check()
        {
            if(i == 1000)
            {
                context.BulkSaveChangesAsync();
                i = 0;
            }
            else
            {
                i++;
            }
        }

        static object _object = new object();
        private static void SaveHistoricCandleStick(IEnumerable<Candlestick> obj)
        {
            lock (_object)
            {

                var list = new List<CandleStickDb>();

                foreach (var candlestick in obj)
                {
                    
                    list.Add(new CandleStickDb(candlestick));
                    //Check();
                }

                context.BulkInsert(list);
            }
        }
    }
}
