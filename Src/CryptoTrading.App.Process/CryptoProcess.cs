using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database.Config;
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
        private List<Symbol> Symbols { get; set; }

        private Task RunningProcess { get; set; }
        private CancellationTokenSource taskToken = new CancellationTokenSource();
        public CryptoProcess(CryptoDbConfigContext context)
        {
            Config = context.CryptoConfigs.First(x => x.Id == 1);
            Config.SetContext(context);
        }

        //Load the positions dictionary and current tickers and map the events.
        public void ReadBinanceData()
        {
            Symbols = AccountConfig.LoadCurrencies();
            var accountPositions = AccountConfig.LoadPositions();
            PositionHelper.AddPositions(Symbols, accountPositions,TradeProcessor.Positions);
            ProcessHelper.WireMarketDataEvents(MarketData, Symbols, Config, Algorithm);
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
            ArchiveHelper.StoreTradesToDb(completedTrades, Config);

        }
        //Start Streaming
        public void StartProcessing()
        {
            RunningProcess = new Task(() => MarketData.StartStream(taskToken));
            RunningProcess.Start();
        }
        //Build the service objects, some properties are linked in the constuctor but they can be set here.

        public void BuildServiceObjects()
        {
            var services = ServiceHelper.BuildServices(Config);

            MarketData = services.GetService<IMarketData>();
            TradeProcessor = services.GetService<ITradeProcessor>(); ;
            Broker = services.GetService<IBroker>(); ;
            Algorithm = services.GetService<IAlgorithm>;
            AccountConfig = services.GetService<IAccountConfig>(); ;
        }
        //Close the app down. Exit out of any open position for a safe shutdown.
        public void CompleteRunningTrades()
        {
            TradeProcessor.CompleteAllTransactions();
            ArchiveAndReport();
        }
        //Reload tickers into the cashe from Binance and update the positions. Email if there are any discrepancies
        public void RefreshSymbols()
        {
            var symbols = AccountConfig.LoadCurrencies();
            if (ProcessHelper.HasSymbols(true,Symbols, symbols, out var newSymbols))
            {
                newSymbols.ForEach(x=>TradeProcessor.Positions.GetPosition(x));
                ProcessHelper.WireMarketDataEvents(MarketData, newSymbols, Config, Algorithm);
            }

            if (ProcessHelper.HasSymbols(false,Symbols, symbols, out var removeSymbols))
            {
                ProcessHelper.RemoveMarketDataEvents(MarketData, removeSymbols, Config);
            }
        }

        public void RefreshPositionsData()
        {
            var positions = AccountConfig.LoadPositions();
            PositionHelper.CheckDifferences(TradeProcessor.Positions, positions);
        }
        //Refresh data from the database, check what properties have changed and act on those that have.
        public void RefreshDatabaseConfig()
        {
            ReadDatabaseConfig();
            if (!Config.EndProcess) return;
            taskToken.Cancel();
            Config.EndProcess = false;
            Config.Update();
        }

        public bool IsRunning => !RunningProcess.IsCompleted;
    }
}
