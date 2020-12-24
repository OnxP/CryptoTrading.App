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
            context.Database.ExecuteSqlCommand("TRUNCATE TABLE myCandlesticks");
            IMarketData marketDate = ServiceProvider.GetService<IMarketData>();
            marketDate.Configure(null);
            marketDate.From = new DateTime(2020, 11, 22);
            List<Symbol> symbols = new List<Symbol>() 
            { 
                Symbol.ETH_BTC,
                Symbol.BTC_USDT,
                Symbol.LTC_BTC,
                Symbol.BNB_BTC,
                Symbol.EOS_BTC,
                Symbol.SYS_BTC,
                Symbol.TRX_BTC,
                Symbol.XRP_BTC 
            };
            List<CandlestickInterval> intervals = new List<CandlestickInterval>()
            {
              CandlestickInterval.Minute
            , CandlestickInterval.Minutes_15
            , CandlestickInterval.Minutes_30
            , CandlestickInterval.Minutes_5
            , CandlestickInterval.Hour
            , CandlestickInterval.Hours_4
            , CandlestickInterval.Day
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
                    marketDate.InitialDataStreamSubscribe(symbol, interval, SaveCandleStick);
                }
            }
        }
        public static CryptoDBContext context = new CryptoDBContext();
        private static void SaveCandleStick(CandlestickEventArgs obj)
        {
            lock (_object)
            {
                context.CandleSticks.Add(new CandleStickDb(obj.Candlestick));
                context.SaveChanges();
            }
        }
        static object _object = new object();
        private static void SaveHistoricCandleStick(IEnumerable<Candlestick> obj)
        {
            lock (_object)
            {
                foreach (var candlestick in obj)
                {
                    context.CandleSticks.Add(new CandleStickDb(candlestick));
                    context.SaveChanges();
                }
            }
        }
    }
}
