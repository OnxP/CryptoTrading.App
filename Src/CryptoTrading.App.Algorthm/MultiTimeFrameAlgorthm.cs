using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.RequestTracker;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace CryptoTrading.App.Algorithm
{
    public class MultiTimeFrameAlgorithm : IAlgorithm
    {
        public ILogger<SimpleAlgorithm> Logger { get; set; }
        public int NumberOfCandleSticksToKeep => 200;
        private readonly QuoteHub<IQuote> _quoteHub;
        private readonly QuoteHub<IQuote> _quoteHubHigh;
        private Symbol _symbol;
        public IMarketStructureStrategy HighLevelStrategy { get; set; }
        public IStrategy TradingStrategy;
        public string KeyValue { get; set; }

        public IConfig Config { get; set; }

        public IMarketStructureResult MarketStructure {get;set;}

        public IExecutionStrategy ExecutionStrategy { get; set; }
        public void Configure(IConfig config)
        {
            Config = config;
        }
        public MultiTimeFrameAlgorithm(IMarketStructureStrategy highLevelStrateg,IStrategy strategies, ILogger<SimpleAlgorithm> logger)
        {
            HighLevelStrategy = highLevelStrateg;
            TradingStrategy = strategies;
            Logger = logger;
            KeyValue = string.IsNullOrEmpty(KeyValue) ? "1" : KeyValue;
        }
        public MultiTimeFrameAlgorithm(IMarketStructureStrategy highLevelStrateg, IStrategy strategies, ILogger<SimpleAlgorithm> logger, IKey key):this(highLevelStrateg,strategies, logger)
        {
            KeyValue = key.KeyValue;
        }

        public void ProcessHistoricMarketDataHigh(IEnumerable<Candlestick> candlesticks)
        {
            foreach (var candle in candlesticks)
             _quoteHubHigh.Add(new Quote
            {
                Timestamp = candle.CloseTime,
                Open = candle.Open,
                High = candle.High,
                Low = candle.Low,
                Close = candle.Close,
                Volume = candle.Volume
            });
            HighLevelStrategy.SetQuotes(_quoteHubHigh);
            Logger.LogInformation(
                $"Added {candlesticks.Count()} historic candlesticks Higher Timeframe for {candlesticks.First().Symbol}");
        }
        public void ProcessHistoricMarketData(IEnumerable<Candlestick> candlesticks)
        {
             foreach (var candle in candlesticks)
                _quoteHub.Add(new Quote
                {
                    Timestamp = candle.CloseTime,
                    Open = candle.Open,
                    High = candle.High,
                    Low = candle.Low,
                    Close = candle.Close,
                    Volume = candle.Volume
                });
            TradingStrategy.SetQuotes(_quoteHub);
            Logger.LogInformation(
                $"Added {candlesticks.Count()} historic candlesticks Medium Timeframe for {candlesticks.First().Symbol}");
        }
        public void ProcessLiveCandleStickHigh(CandlestickEventArgs candlestickEventArgs)
        {
            try
            {
                if (!candlestickEventArgs.IsFinal) return;
                _quoteHubHigh.Add(new Quote
                {
                    Timestamp = candlestickEventArgs.Candlestick.CloseTime,
                    Open = candlestickEventArgs.Candlestick.Open,
                    High = candlestickEventArgs.Candlestick.High,
                    Low = candlestickEventArgs.Candlestick.Low,
                    Close = candlestickEventArgs.Candlestick.Close,
                    Volume = candlestickEventArgs.Candlestick.Volume
                });
                MarketStructure = HighLevelStrategy.Calculate();
            }
            catch (Exception e)
            {
                Logger.LogError(0, e, "Algo Error Occurred");
            }
        }
        public void ProcessLiveCandleStick(CandlestickEventArgs candlestickEventArgs)
        {
            try
            {
                if (!candlestickEventArgs.IsFinal) return;
                _quoteHub.Add(new Quote
                {
                    Timestamp = candlestickEventArgs.Candlestick.CloseTime,
                    Open = candlestickEventArgs.Candlestick.Open,
                    High = candlestickEventArgs.Candlestick.High,
                    Low = candlestickEventArgs.Candlestick.Low,
                    Close = candlestickEventArgs.Candlestick.Close,
                    Volume = candlestickEventArgs.Candlestick.Volume
                });
                var strategyResult = TradingStrategy.Calculate(MarketStructure,out IExecutionStrategy ExecutionStrategy);
                if (strategyResult.PostTrade)
                    RequestTracker.Instance.Add(candlestickEventArgs.Candlestick.Symbol, new TradeRequest(strategyResult,ExecutionStrategy,_symbol,candlestickEventArgs.Candlestick.CloseTime), KeyValue);
            }
            catch(Exception e)
            {
                Logger.LogError(0,e,"Algo Error Occurred");
            }
        }

        public void Subscribe(Symbol symbol, IMarketDataEvents marketData)
        {
            _symbol = symbol;
            marketData.InitialDataLoadSubscribe(symbol, CandlestickInterval.Hours_4, ProcessHistoricMarketDataHigh);
            marketData.InitialDataStreamSubscribe(symbol, CandlestickInterval.Hours_4, ProcessLiveCandleStickHigh);
            marketData.InitialDataLoadSubscribe(symbol, CandlestickInterval.Minutes_15, ProcessHistoricMarketData);
            marketData.InitialDataStreamSubscribe(symbol, CandlestickInterval.Minutes_15, ProcessLiveCandleStick);
        }
    }
}
