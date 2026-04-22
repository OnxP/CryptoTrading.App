using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.RequestTracker;
using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

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

        public void ProcessHistoricMarketDataHigh(IEnumerable<ExchangeCandlestick> candlesticks)
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
        public void ProcessHistoricMarketData(IEnumerable<ExchangeCandlestick> candlesticks)
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
        public void ProcessLiveCandleStickHigh(ExchangeCandlestickEvent candlestickEventArgs)
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
        public void ProcessLiveCandleStick(ExchangeCandlestickEvent candlestickEventArgs)
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
                var strategyResult = TradingStrategy.Calculate(MarketStructure);
                if (strategyResult.Item1.PostTrade)
                    RequestTracker.Instance.Add(candlestickEventArgs.Candlestick.Symbol, new TradeRequest(strategyResult.Item1,strategyResult.Item2,_symbol,candlestickEventArgs.Candlestick.CloseTime), KeyValue);
            }
            catch(Exception e)
            {
                Logger.LogError(0,e,"Algo Error Occurred");
            }
        }

        public void Subscribe(Symbol symbol, IMarketDataEvents marketData)
        {
            _symbol = symbol;
            marketData.InitialDataLoadSubscribe(symbol, CandleInterval.Hour_4, ProcessHistoricMarketDataHigh);
            marketData.InitialDataStreamSubscribe(symbol, CandleInterval.Hour_4, ProcessLiveCandleStickHigh);
            marketData.InitialDataLoadSubscribe(symbol, CandleInterval.Minute_15, ProcessHistoricMarketData);
            marketData.InitialDataStreamSubscribe(symbol, CandleInterval.Minute_15, ProcessLiveCandleStick);
        }
    }
}
