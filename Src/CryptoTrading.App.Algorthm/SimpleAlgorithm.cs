using Binance;
using Binance.Client;
using CryptoTrading.App.Algorithm.TradingStrategies;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using CryptoTrading.App.Process;

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
        public void Configure(IConfig config)
        {
            throw new NotImplementedException();
        }
        public SimpleAlgorithm(ITradingStrategy strategies, ILogger<SimpleAlgorithm> logger, IStopLimitTracker stopLimitTrackers)
        { 
            TradingStrategies = strategies;
            _candleSticks = new CandleStickDictionary(NumberOfCandleSticksToKeep);
            StopLimitTrackers = stopLimitTrackers;
            Logger = logger;
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
            var result = CalculateTradeStrategies(candlesticks.First().Symbol,
                candlesticks.First().Interval.AsString(), candlesticks.Last().CloseTime);
        }

        public void ProcessLiveCandleStick(CandlestickEventArgs candlestickEventArgs)
        {
            try
            {
                _candleSticks.Add(candlestickEventArgs.Candlestick);
                Logger.LogInformation($"Processing Strategies for {candlestickEventArgs.Candlestick.Symbol} at {candlestickEventArgs.Candlestick.CloseTime:yyyy/MM/dd hh:mm}");
                var request = CalculateTradeStrategies(candlestickEventArgs.Candlestick.Symbol, candlestickEventArgs.Candlestick.Interval.AsString(), candlestickEventArgs.Candlestick.CloseTime);
                Logger.LogInformation($"Finished processing for Strategies for {candlestickEventArgs.Candlestick.Symbol} at {candlestickEventArgs.Candlestick.CloseTime:yyyy/MM/dd hh:mm}");
                if (request==null) return;
                if (request.SellPercentage <= 0) return;
                MessageBroker.Instance.Publish(KeyValue, this, request);
            }
            catch(Exception e)
            {
                Logger.LogError(0,e,"Algo Error Occurred");
            }
        }

        public ITradeRequest CalculateTradeStrategies(string symbol, string interval, DateTime closeTime)
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
            //Logger.LogInformation($"Finished processing strategy {strategy} with result {result}");
            var request = RequestBuilder.BuildTradeRequest(result, symbol, _candleSticks.Current.Close, closeTime, StopLimitTrackers);

            return request;
        }
    }
}
