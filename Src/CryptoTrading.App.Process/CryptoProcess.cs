using System;
using System.Collections.Generic;
using System.Configuration.Internal;
using System.Text;
using Binance;
using CryptoTrading.App.Algorithm;
using CryptoTrading.App.Core;

namespace CryptoTrading.App.Process
{
    public class CryptoProcess
    {
        //TODO Email and Logging
        private IMarketData MarketData { get; }
        private ITradeProcessor TradeProcessor { get; }
        private IAlgorithm Algorithm { get; }
        private IConfig Config { get; }
        private IAccountConfig AccountConfig { get; }
        private List<Symbol> Symbols { get; set; }
        public CryptoProcess(IMarketData marketData, ITradeProcessor tradeProcessor, IAlgorithm algorithm, IConfig config, IAccountConfig accountConfig)
        {
            MarketData = marketData;
            TradeProcessor  = tradeProcessor;
            Algorithm = algorithm; 
            Config = config;    
            AccountConfig = accountConfig;
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
            Config.Load();
            MarketData.Configure(Config);
            Algorithm.Configure(Config);
        }
        //Archive Trade Data to the database and generate a report, to be emailed.
        public void ArchiveAndReport()
        {
            var completedTrades = ProcessHelper.GetCompletedTrades(TradeProcessor);
            ArchiveHelper.StoreTradesToDb(completedTrades);
            ArchiveHelper.EmailTrades(completedTrades);
            ArchiveHelper.ClearCompletedTrades(completedTrades);
        }
        //Start Streaming
        public void StartProcessing()
        {
            MarketData.StartStream();
            IsRunning = true;
        }
        //Build the service objects, some properties are linked in the constuctor but they can be set here.
        public void BuildServiceObjects()
        {
            throw new NotImplementedException();
        }
        //Close the app down. Exit out of any open position for a safe shutdown.
        public void CompleteRunningTrades()
        {
            IsRunning = false;
            TradeProcessor.CompleteAllTransactions();
            ArchiveAndReport();
        }
        //Reload tickers into the cashe from Binance and update the positions. Email if there are any discrepancies
        public void RefreshSymbols()
        {
            var symbols = AccountConfig.LoadCurrencies();
            if (ProcessHelper.HasSymbols(true,Symbols, symbols, out var newSymbols))
            {
                ProcessHelper.WireMarketDataEvents(MarketData, newSymbols, Config, Algorithm);
            }

            if (ProcessHelper.HasSymbols(false,Symbols, symbols, out var removeSymbols))
            {
                ProcessHelper.RemoveMarketDataEvents(MarketData, removeSymbols, Config, Algorithm);
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
            IsRunning = Config.EndProcess;
        }

        public bool IsRunning { get; set; }
    }
}
