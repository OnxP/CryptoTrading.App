using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.MarketData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            HistoricalMarketData marketDate = ServiceProvider.GetService<IMarketData>() as HistoricalMarketData;

            // PR 6f: symbol enumeration goes through IExchangeProvider — the
            // bundled IBinanceApi is no longer touched anywhere in DatabaseLoad.
            var exchangeProvider = ServiceProvider.GetService<IExchangeProvider>();
            var sym = await exchangeProvider.GetSymbolsAsync().ConfigureAwait(false);
            var symbols = sym.Where(x => x.QuoteAsset == "USDT").ToList();

            // PR 5b: HistoricalMarketData no longer needs an IBinanceApi passed in;
            // it resolves IExchangeProvider from DI. Configure(IConfig) is a no-op
            // and only stays on the interface for back-compat.
            marketDate.From = new DateTime(2026, 02, 28, 23, 00, 00);
            marketDate.To = new DateTime(2026, 04, 01, 23, 00, 00);

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
            // PR 6e: MissingCandleDetector takes IExchangeProvider directly. PR 6f
            // routed symbol enumeration through the same provider, so DatabaseLoad
            // no longer references IBinanceApi at all.
            var gapLogger = ServiceProvider.GetService<ILogger<MissingCandleDetector>>();
            var detector = new MissingCandleDetector(exchangeProvider, gapLogger);
            var symbolNames = symbols.Select(s => s.Ticker).ToList();

            // Process all symbols concurrently — each symbol checks all its intervals in
            // parallel too.  Concurrency towards the Binance API is capped by the
            // semaphore inside MissingCandleDetector (default: 10 simultaneous calls).
            var gapTasks = symbolNames.Select(symbolName =>
                detector.FillMissingForSymbolAsync(symbolName, intervals, marketDate.From, marketDate.To));
            await Task.WhenAll(gapTasks).ConfigureAwait(false);
        }
    }
}
