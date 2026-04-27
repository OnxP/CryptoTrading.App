using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.MarketData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.Entity;
using System.IO;
using System.Linq;
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
                .AddBinance(useSingleCombinedStream: true)
                .AddHistoricMarketData()
                // Configure logging.
                .AddLogging(builder => builder // configure logging.
                        .SetMinimumLevel(LogLevel.Trace)
                        .AddConsole()
                        )
                .BuildServiceProvider();
            //context.Database.ExecuteSqlCommand("TRUNCATE TABLE CandleStickDbs");
            HistoricalMarketData marketDate = ServiceProvider.GetService<IMarketData>() as HistoricalMarketData;

            var Api = ServiceProvider.GetService<IBinanceApi>();
            /* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            //var symbols = await Api.GetSymbolsAsync().ConfigureAwait(false).Where(x=> x.QuoteAsset.Symbol.Contains("USD")).ToList();//count
            /* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            var sym = await Api.GetSymbolsAsync().ConfigureAwait(false);
            var symbols = sym.Where(x => x.QuoteAsset.Symbol == "USDT").ToList();//count

            // PR 5b: HistoricalMarketData no longer needs an IBinanceApi passed in;
            // it resolves IExchangeProvider from DI. Configure(IConfig) is a no-op
            // and only stays on the interface for back-compat.
            marketDate.From = new DateTime(2026, 02, 28,23,00,00);
            marketDate.To = new DateTime(2026, 04, 01,23,00,00);

            // PR 6b: MissingCandleDetector / CandleGapFiller now take neutral CandleInterval.
            List<CandleInterval> intervals = new List<CandleInterval>()
            {
              CandleInterval.Hour_4,
              CandleInterval.Hour_1,
              CandleInterval.Minute_15,
              CandleInterval.Minute_5
              , CandleInterval.Minute_1
            };

            // --- Gap fill: check the DB per symbol and download only what is missing ---
            // PR 6e: MissingCandleDetector now takes IExchangeProvider directly —
            // no more bundled IBinanceApi inside the gap-fill pipeline. Symbol
            // enumeration above (Api.GetSymbolsAsync) still uses the bundled
            // IBinanceApi; that boundary moves in a later slice.
            var gapLogger = ServiceProvider.GetService<ILogger<MissingCandleDetector>>();
            var exchangeProvider = ServiceProvider.GetService<IExchangeProvider>();
            var detector = new MissingCandleDetector(exchangeProvider, gapLogger);
            var symbolNames = symbols.Select(s => s.ToString()).ToList();

            // Process all symbols concurrently — each symbol checks all its intervals in
            // parallel too.  Concurrency towards the Binance API is capped by the
            // semaphore inside MissingCandleDetector (default: 10 simultaneous calls).
            var gapTasks = symbolNames.Select(symbolName =>
                detector.FillMissingForSymbolAsync(symbolName, intervals, marketDate.From, marketDate.To));
            await Task.WhenAll(gapTasks).ConfigureAwait(false);

            // --- Full download for anything outside the gap-fill scope ---
            //subscribe to several symbols
            //AddEvents(marketDate as AbstractMarketData, symbols, intervals);

            //await marketDate.StartStream();

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

        private static void AddEvents(AbstractMarketData marketDate, List<Symbol> symbols, List<CandleInterval> intervals)
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
        public static CryptoDbContext context = new CryptoDbContext();
        private static void AddCandleStick(ExchangeCandlestickEvent obj)
        {
            lock (_object)
            {
                // PR 5h: neutral CandleStickDb ctor — no bridge needed.
                context.CandleSticks.Add(new CandleStickDb(obj.Candlestick));
                Check();
            }
        }
        public static int i = 0;
        public static int j = 0;
        private static void Check()
        {
            if(i == 10000)
            {
                context.BulkSaveChangesAsync();
                i = 0;
            }
            else
            {
                i++;
            }
        }

        static List<CandleStickDb> list = new List<CandleStickDb>();
        private static void Check(List<CandleStickDb> sticks)
        {
            list.AddRange(sticks);
            if (list.Count >= 10000)
            {
                context.BulkInsert(list);
                list.Clear();
            }
        }

        static object _object = new object();
        private static void SaveHistoricCandleStick(IEnumerable<ExchangeCandlestick> obj)
        {
            lock (_object)
            {
                var sticks = obj.Select(x => new CandleStickDb(x)).ToList();
                Check(sticks);
            }
        }
    }
}