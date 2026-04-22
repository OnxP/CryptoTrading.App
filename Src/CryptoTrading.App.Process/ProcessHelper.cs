using System;
using System.Collections.Generic;
using System.Linq;
using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Strategy;
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
                algorithm.Subscribe(symbol,marketData);
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

        // Operates on neutral pair strings (e.g. "BTCUSDT") so the caller can
        // stay on the exchange-agnostic side; Binance.Symbol resolution happens
        // later, only when dispatching to market-data/algorithm consumers.
        public static bool HasSymbols(bool added, List<string> currentSymbols, List<string> newSymbols, out List<string> symbols)
        {
            symbols = added ? newSymbols.Except(currentSymbols).ToList() : currentSymbols.Except(newSymbols).ToList();
            return symbols.Any();
        }

        
    }
}
