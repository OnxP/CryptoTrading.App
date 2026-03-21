using System;
using System.IO;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.MarketData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.Entity;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DateTime = System.DateTime;

namespace CryptoTrading.App.DatabaseLoad
{
    class Program
    {
        public static async Task Main(string[] args)
        {

            DbProviderFactories.RegisterFactory("System.Data.SqlClient", System.Data.SqlClient.SqlClientFactory.Instance);
            Database.SetInitializer<CryptoDbContext>(null);
            var Configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", true, false)
                .AddUserSecrets<Program>() // for access to API key and secret.
                .Build();

            // Configure services.
            var ServiceProvider = new ServiceCollection()
                .AddHistoricMarketData()
                // Configure logging.
                .AddLogging(builder => builder // configure logging.
                        .SetMinimumLevel(LogLevel.Trace)
                        .AddConsole()
                        )
                .BuildServiceProvider();
            //context.Database.ExecuteSqlCommand("TRUNCATE TABLE CandleStickDbs");
            HistoricalMarketData marketDate = ServiceProvider.GetService<IMarketData>() as HistoricalMarketData;

            var _provider = ServiceProvider.GetService<IExchangeProvider>();
            /* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            //var symbols = await _provider.GetSymbolsAsync().ConfigureAwait(false).Where(x=> x.QuoteAsset == "USD").ToList();//count
            /* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            var sym = await _provider.GetSymbolsAsync().ConfigureAwait(false);
            var symbols = sym.Where(x => x.QuoteAsset == "BTC").ToList();//count

            marketDate.Configure(_provider);
            marketDate.From = new DateTime(2025, 6, 01);
            marketDate.To = new DateTime(2025, 07,01);

            List<CandleInterval> intervals = new List<CandleInterval>()
            {
              CandleInterval.Minute_15,
              CandleInterval.Minute_5
              , CandleInterval.Minute_1
            };

            //subscribe to several symbols
            AddEvents(marketDate as AbstractMarketData, symbols,intervals);

            marketDate.StartStream();

/* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            //var res = await run(marketDate).ConfigureAwait(false);
            lock (_object)
            {
                context.Dispose();
            }
        }

        private static async Task<int> run(IMarketData marketData)
        {
            await RunMarketData(marketData).ConfigureAwait(false);
            return 1;
        }

        private static async Task RunMarketData(IMarketData marketData)
        {
            var con = marketData.GetTaskController();
            con.Begin();
            await con.Task.ConfigureAwait(false);
        }

        private static void AddEvents(AbstractMarketData marketDate, List<ExchangeSymbol> symbols, List<CandleInterval> intervals)
        {
            foreach (var symbol in symbols)
            {
                foreach (var interval in intervals)
                {
                    marketDate.InitialDataLoadSubscribe(symbol.Ticker, interval, SaveHistoricCandleStick);
                    marketDate.InitialDataStreamSubscribe(symbol.Ticker, interval, AddCandleStick);
                }
            }
        }
        public static CryptoDbContext context = new CryptoDbContext();
        private static void AddCandleStick(ExchangeCandlestickEvent obj)
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
        private static void SaveHistoricCandleStick(IEnumerable<ExchangeCandlestick> obj)
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
