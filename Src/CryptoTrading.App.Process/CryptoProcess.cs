using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Binance;
using Binance.Utility;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database.Config;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Monitor;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoTrading.App.Process
{
    public class CryptoProcess :IProcess
    {
        private IMarketData MarketData { get; set; }
        private ITradeProcessor TradeProcessor { get; set; }
        private IBroker Broker { get; set; }
        private Func<IAlgorithm> Algorithm { get; set; }
        private IConfig Config { get; }
        private IAccountConfig AccountConfig { get; set; }
        // Neutral pair strings (e.g. "BTCUSDT"). Exchange-specific info
        // (Binance.Symbol) is resolved at the boundary when dispatching to
        // the algorithm / market-data code, which still consumes it.
        private List<string> Symbols { get; set; }
        public CryptoProcess(CryptoDbConfigContext context)
        {
            Config = context.CryptoConfigs.First(x => x.Id == 1);
            Config.SetContext(context);
        }

        //Load the positions dictionary and current tickers and map the events.
        public async void ReadBinanceData()
        {
            Symbols = await AccountConfig.LoadCurrencies();
            var accountPositions = await AccountConfig.LoadPositions();
            // PositionHelper.AddPositions + WireMarketDataEvents still consume
            // Binance.Symbol (algorithm/market-data internals). Resolve the
            // neutral pair strings back to Binance.Symbol here — one-shot at
            // the boundary — until those consumers are migrated themselves.
            var binanceSymbols = ResolveBinanceSymbols(Symbols);
            PositionHelper.AddPositions(binanceSymbols, accountPositions, TradeProcessor.Positions);
            ProcessHelper.WireMarketDataEvents(MarketData, binanceSymbols, Config, Algorithm);
        }

        private static List<Symbol> ResolveBinanceSymbols(IEnumerable<string> pairs)
        {
            var list = new List<Symbol>();
            foreach (var pair in pairs)
            {
                var s = Symbol.Cache?.Get(pair);
                if (s != null) list.Add(s);
            }
            return list;
        }

        //Load the config from the database and set it in the services.
        public void ReadDatabaseConfig()
        {
            //TODO this is a refresh of the config object so it should have all the details about which SQL server it should be connected to.
            Config.Load();
            //MarketData.Configure(Config);
            //Algorithm.Configure(Config);
        }
        //Archive Trade Data to the database and generate a report, to be emailed.
        public void ArchiveAndReport()
        {
            var completedTrades = ProcessHelper.GetCompletedTrades(TradeProcessor,Config);
            ArchiveHelper.EmailTrades(completedTrades, Config);//need to do positions as well.
            //ArchiveHelper.StoreTradesToDb(completedTrades, Config);

        }

        public bool IsRunning => Controller?.IsActive ?? true;
        private ITaskController Controller { get; set; }
        public async Task StartProcessing()
        {
            MarketData.Configure(Config);
            Controller = MarketData.GetTaskController();
            Controller.Begin();
            await Controller.Task;
        }
        //Build the service objects, some properties are linked in the constuctor but they can be set here.

        public void BuildServiceObjects()
        {
            var services = ServiceHelper.BuildServices(Config);

            MarketData = services.GetService<IMarketData>();
            TradeProcessor = services.GetService<ITradeProcessor>();
            Broker = services.GetService<IBroker>(); 
            Algorithm = services.GetService<IAlgorithm>;
            AccountConfig = services.GetService<IAccountConfig>();

            TradeProcessor.Configure(Config);
        }
        //Close the app down. Exit out of any open position for a safe shutdown.
        public void CompleteRunningTrades()
        {
            TradeProcessor.CompleteAllTransactions();
            ArchiveAndReport();
        }
        //Reload tickers into the cashe from Binance and update the positions. Email if there are any discrepancies
        public async void RefreshSymbols()
        {
            var symbols = await AccountConfig.LoadCurrencies();
            if (ProcessHelper.HasSymbols(true, Symbols, symbols, out var newSymbols))
            {
                // newSymbols are pair strings; GetPosition wants an asset key and
                // ProcessHelper.WireMarketDataEvents still needs Binance.Symbol.
                newSymbols.ForEach(x => TradeProcessor.Positions.GetPosition(x));
                ProcessHelper.WireMarketDataEvents(MarketData, ResolveBinanceSymbols(newSymbols), Config, Algorithm);
            }

            if (ProcessHelper.HasSymbols(false, Symbols, symbols, out var removeSymbols))
            {
                ProcessHelper.RemoveMarketDataEvents(MarketData, ResolveBinanceSymbols(removeSymbols), Config);
            }
            MarketData.Configure(Config);
        }

        public async void RefreshPositionsData()
        {
            var positions = await AccountConfig.LoadPositions();
            PositionHelper.CheckDifferences(TradeProcessor.Positions, positions);
        }

        // NOTE: Algorithm.Subscribe(Symbol) still consumes Binance.Symbol; the
        // neutral string list is converted at call sites only. When the
        // algorithm consumer migrates (PR 5) this helper can go.
        //Refresh data from the database, check what properties have changed and act on those that have.
        public void RefreshDatabaseConfig()
        {
            ReadDatabaseConfig();
            if (!Config.EndProcess) return;
            Controller.CancelAsync();
            Config.EndProcess = false;
            Config.Update();
        }
    }
}
