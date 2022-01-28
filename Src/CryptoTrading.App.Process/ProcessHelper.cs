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

        public static List<ITrade> GetCompletedTrades(ITradeProcessor tradeProcessor)
        {
            var completedTrades = tradeProcessor.GetCompletedTrades();
            tradeProcessor.ClearInactiveTrades();
            return completedTrades;
        }

        public static bool HasSymbols(bool added, List<Symbol> currentSymbols, List<Symbol> newSymbols, out List<Symbol> symbols)
        {
            symbols = added ? newSymbols.Except(currentSymbols).ToList() : currentSymbols.Except(newSymbols).ToList();
            return symbols.Any();
        }

        
    }
}
