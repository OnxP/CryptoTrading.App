using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Binance;
using CryptoTrading.App.Algorithm;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Process
{
    internal class ProcessHelper
    {
        public static void WireMarketDataEvents(IMarketDataEvents marketData, List<Symbol> symbols, IConfig config, IAlgorithm algorithm)
        {
            var interval = config.Interval;

            foreach (var symbol in symbols)
            {
                marketData.InitialDataLoadSubscribe(symbol, interval, algorithm.ProcessHistoricMarketData);
                marketData.InitialDataStreamSubscribe(symbol, interval, algorithm.ProcessLiveCandleStick);
            }
        }
        public static void RemoveMarketDataEvents(IMarketDataEvents marketData, List<Symbol> removeSymbols, IConfig config, IAlgorithm algorithm)
        {
            var interval = config.Interval;
            foreach (var symbol in removeSymbols)
            {
                marketData.InitialDataLoadUnSubscribe(symbol, interval);
                marketData.InitialDataStreamUnSubscribe(symbol, interval, algorithm.ProcessLiveCandleStick);
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
