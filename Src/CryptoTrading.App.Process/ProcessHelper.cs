using System;
using System.Collections.Generic;
using System.Linq;
using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Process
{
    internal class ProcessHelper
    {
        public static void WireMarketDataEvents(IMarketDataEvents marketData, List<Symbol> symbols, IConfig config, Func<IAlgorithm> getAlgorithm)
        {
            var interval = config.Interval;

            foreach (var symbol in symbols)
            {
                //need to create a unique instance of algo
                var algorithm = getAlgorithm.Invoke();
                algorithm.Configure(config);
                marketData.InitialDataLoadSubscribe(symbol, interval, algorithm.ProcessHistoricMarketData);
                marketData.InitialDataStreamSubscribe(symbol, interval, algorithm.ProcessLiveCandleStick);
            }
        }
        public static void RemoveMarketDataEvents(IMarketDataEvents marketData, List<Symbol> removeSymbols, IConfig config)
        {
            var interval = config.Interval;
            foreach (var symbol in removeSymbols)
            {
                marketData.InitialDataLoadUnSubscribe(symbol, interval);
                marketData.InitialDataStreamUnSubscribe(symbol, interval);
            }
        }

        public static List<HistoricTrades> GetCompletedTrades(ITradeProcessor tradeProcessor, IConfig config)
        {
            var completedTrades = tradeProcessor.GetCompletedTrades();
            tradeProcessor.ClearInactiveTrades();
            var factory = new ArchiveTradeFactory(config);
            var historicTrades = new List<HistoricTrades>();
            completedTrades.ForEach(x => factory.CreateHistoricTrades(x,historicTrades));
            return historicTrades;
        }

        public static bool HasSymbols(bool added, List<Symbol> currentSymbols, List<Symbol> newSymbols, out List<Symbol> symbols)
        {
            symbols = added ? newSymbols.Except(currentSymbols).ToList() : currentSymbols.Except(newSymbols).ToList();
            return symbols.Any();
        }

        
    }
}
