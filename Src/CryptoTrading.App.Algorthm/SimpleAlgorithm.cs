using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace CryptoTrading.App.Algorithm
{
    public class SimpleAlgorithm : IAlgorithm
    {
        public ILogger<SimpleAlgorithm> Logger { get; set; }
        public int NumberOfCandleSticksToKeep => TradingStrategies.OutputLength;
        private readonly CandleStickDictionary _candleSticks;
        public ITradingStrategy TradingStrategies;
        public IStopLimitTracker StopLimitTrackers { get; set; } 
        public string KeyValue { get; set; }

        public IConfig Config { get; set; }
        public void Configure(IConfig config)
        {
            Config = config;
        }
        public SimpleAlgorithm(ITradingStrategy strategies, ILogger<SimpleAlgorithm> logger, IStopLimitTracker stopLimitTrackers)
        { 
            TradingStrategies = strategies;
            _candleSticks = new CandleStickDictionary(NumberOfCandleSticksToKeep);
            StopLimitTrackers = stopLimitTrackers;
            Logger = logger;
            KeyValue = string.IsNullOrEmpty(KeyValue) ? "1" : KeyValue;
        }
        public SimpleAlgorithm(ITradingStrategy strategies, ILogger<SimpleAlgorithm> logger, IStopLimitTracker stopLimitTrackers, IKey key):this(strategies,logger,stopLimitTrackers)
        {
            KeyValue = key.KeyValue;
        }

        public void ProcessHistoricMarketData(IEnumerable<Candlestick> candlesticks)
        {
            //want to reduce dependancy on the candle stick object=> may need to create my own.
            _candleSticks.AddRange(candlesticks);
            if (!_candleSticks.Ready) return;

            Logger.LogInformation(
                $"Added {candlesticks.Count()} historic candlesticks for {candlesticks.First().Symbol}");
            StopLimitTrackers.EndDateTime = candlesticks.First().CloseTime;
            //var result = CalculateTradeStrategies(candlesticks.First().Symbol,
            //     candlesticks.Last().CloseTime, candlesticks.Last().Volume);
        }

        public void ProcessLiveCandleStick(CandlestickEventArgs candlestickEventArgs)
        {
            try
            {
                if (!candlestickEventArgs.IsFinal) return;
                _candleSticks.Add(candlestickEventArgs.Candlestick);
                //Logger.LogInformation($"Processing Strategies for {candlestickEventArgs.Candlestick.Symbol} at {candlestickEventArgs.Candlestick.CloseTime:yyyy/MM/dd hh:mm}");
                var request = CalculateTradeStrategies(candlestickEventArgs.Candlestick.Symbol, candlestickEventArgs.Candlestick.CloseTime, candlestickEventArgs.Candlestick.Volume);
                Logger.LogInformation($"Finished processing for Strategies for {candlestickEventArgs.Candlestick.Symbol} at {candlestickEventArgs.Candlestick.CloseTime:yyyy/MM/dd hh:mm:ss}");
                if (request==null) return;
                if (request.Amount <= 0) return;
                MessageBroker.Instance.Publish(KeyValue, this, request);
            }
            catch(Exception e)
            {
                Logger.LogError(0,e,"Algo Error Occurred");
            }
        }

        public ITradeRequest CalculateTradeStrategies(string symbol, DateTime closeTime,decimal volume )
        {
            if (!_candleSticks.Ready)
            {
                return null;
            }

            if (_candleSticks.HasMissing)
            {
                //TODO Trigger backfill.
                return null;
            }

            var result = TradingStrategies.Calculate(_candleSticks, StopLimitTrackers);
            
            if(result == 0) return RequestBuilder.BuildTradeRequest(result, Config.UseFixedAmount, symbol, _candleSticks.Current.Close, closeTime, StopLimitTrackers, volume , (decimal)Config.PercentDailyVolume);
            
            result = (Config.UseFixedAmount?Config.FixedAmount:1) / Config.NoOfTrades;
            //need access to config here.
            var request = RequestBuilder.BuildTradeRequest(result, Config.UseFixedAmount, symbol, _candleSticks.Current.Close, closeTime, StopLimitTrackers, volume , (decimal)Config.PercentDailyVolume);

            return request;
        }

    }
}
