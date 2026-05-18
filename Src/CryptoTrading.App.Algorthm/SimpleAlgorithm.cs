using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.RequestTracker;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm
{
    public class SimpleAlgorithm : IAlgorithm
    {
        public ILogger<SimpleAlgorithm> Logger { get; set; }
        public int NumberOfCandleSticksToKeep => TradingStrategies.OutputLength;
        private readonly CandleStickDictionary _candleSticks;
        public ITradingStrategy TradingStrategies;
        public IStopLimitTracker StopLimitTracker { get; set; } 
        public string KeyValue { get; set; }

        public IConfig Config { get; set; }
        public void Configure(IConfig config)
        {
            Config = config;
        }
        public SimpleAlgorithm(ITradingStrategy strategies, ILogger<SimpleAlgorithm> logger, IStopLimitTracker stopLimitTracker)
        { 
            TradingStrategies = strategies;
            _candleSticks = new CandleStickDictionary(NumberOfCandleSticksToKeep);
            StopLimitTracker = stopLimitTracker;
            Logger = logger;
            KeyValue = string.IsNullOrEmpty(KeyValue) ? "1" : KeyValue;
        }
        public SimpleAlgorithm(ITradingStrategy strategies, ILogger<SimpleAlgorithm> logger, IStopLimitTracker stopLimitTracker, IKey key):this(strategies,logger,stopLimitTracker)
        {
            KeyValue = key.KeyValue;
        }

        public void ProcessHistoricMarketData(IEnumerable<ExchangeCandlestick> candlesticks)
        {
            //want to reduce dependancy on the candle stick object=> may need to create my own.
            _candleSticks.AddRange(candlesticks);
            if (!_candleSticks.Ready) return;

            Logger.LogInformation(
                $"Added {candlesticks.Count()} historic candlesticks for {candlesticks.First().Symbol}");
            StopLimitTracker.EndDateTime = candlesticks.First().CloseTime;
            //var result = CalculateTradeStrategies(candlesticks.First().Symbol,
            //     candlesticks.Last().CloseTime, candlesticks.Last().Volume);
        }



        public void ProcessLiveCandleStick(ExchangeCandlestickEvent candlestickEventArgs)
        {
            try
            {
                CandleStickTracker.Instance.UpdateCandleStick(candlestickEventArgs);
                if (!candlestickEventArgs.IsFinal) return;
                _candleSticks.Add(candlestickEventArgs.Candlestick);
                var signal = CalculateTradeSignal(candlestickEventArgs.Candlestick.Symbol, candlestickEventArgs.Candlestick.CloseTime, candlestickEventArgs.Candlestick.QuoteVolume, candlestickEventArgs.Candlestick.NumberOfTrades);
                if (signal == null) return;
                if (signal.Quantity <= 0) return;
                RequestTracker.Instance.Add(candlestickEventArgs.Candlestick.Symbol, signal, KeyValue);
            }
            catch(Exception e)
            {
                Logger.LogError(0,e,"Algo Error Occurred");
            }
        }

        public ITradeSignal CalculateTradeSignal(string symbol, DateTime closeTime, decimal volume, long numberOfTrades)
        {
            if (!_candleSticks.Ready)
                return null;

            if (_candleSticks.HasMissing)
                return null;

            var result = TradingStrategies.Calculate(_candleSticks, StopLimitTracker);

            if (result == 0) return null;

            result = ((Config.UseFixedAmount ? Config.FixedAmount : 1) / Config.NoOfTrades);
            var close = _candleSticks.Current.Close;

            return new TradeSignal
            {
                Symbol = symbol,
                Direction = TradeDirection.Long,
                Quantity = (decimal)result / close,
                SignalTime = closeTime,
                EntryPrice = close,
                StopLoss = StopLimitTracker?.StopLimitPrice ?? 0m,
                TakeProfit = 0m,
                AtrAtSignal = 0m,
                InitialRisk = 0m
            };
        }

        public void Subscribe(Symbol symbol, IMarketDataEvents marketData)
        {
            marketData.InitialDataLoadSubscribe(symbol, CandleInterval.Minute_15, ProcessHistoricMarketData);
            marketData.InitialDataStreamSubscribe(symbol, CandleInterval.Minute_15, ProcessLiveCandleStick);
        }
    }
}
