using System;
using System.Collections.Generic;
using System.Linq;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using IAlgorithm = CryptoTrading.App.Core.Strategy.IAlgorithm;

namespace CryptoTrading.App.Process
{
    internal class ProcessHelper
    {
        public static void WireMarketDataEvents(IMarketDataEvents marketData, List<ExchangeSymbol> symbols, IConfig config, Func<IAlgorithm> getAlgorithm)
        {
            var interval = config.Interval;

            foreach (var symbol in symbols)
            {
                //need to create a unique instance of algo
                var algorithm = getAlgorithm.Invoke();
                algorithm.Configure(config);
                algorithm.Subscribe(symbol,marketData);
            }
        }
        public static void RemoveMarketDataEvents(IMarketDataEvents marketData, List<ExchangeSymbol> removeSymbols, IConfig config)
        {
            var interval = config.Interval;
            foreach (var symbol in removeSymbols)
            {
                marketData.InitialDataLoadUnSubscribe(symbol.Ticker, interval);
                marketData.InitialDataStreamUnSubscribe(symbol.Ticker, interval);
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

        public static bool HasSymbols(bool added, List<ExchangeSymbol> currentSymbols, List<ExchangeSymbol> newSymbols, out List<ExchangeSymbol> symbols)
        {
            symbols = added ? newSymbols.Except(currentSymbols).ToList() : currentSymbols.Except(newSymbols).ToList();
            return symbols.Any();
        }

        
    }
}
