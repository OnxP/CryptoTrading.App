using Binance;
using Binance.Client;
using CryptoTrading.App.Algorthm.TradingStrategies;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.TradeRequest;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorthm
{
    public class SimpleAlgorthm : IAlgorthm
    {
        public ILogger<SimpleAlgorthm> Logger { get; set; }
        public int NumberOfCandleSticksToKeep => tradingStrategies.OutputLength;
        private OrderedFixedLengthList _closePrices;
        public ITradingStrategy tradingStrategies;
        public SimpleAlgorthm(ITradingStrategy strategies, ILogger<SimpleAlgorthm> logger)
        { 
            tradingStrategies = strategies;
            _closePrices = new OrderedFixedLengthList(NumberOfCandleSticksToKeep);
            Logger = logger;
        }

        public void ProcessHistoricMarketData(IEnumerable<Candlestick> candlesticks)
        {
            //want to reduce dependancy on the candle stick object=> may need to create my own.
            _closePrices.AddRange(candlesticks.Select(x => x.Close));
            Logger.LogInformation($"Added {candlesticks.Count()} historic candlesticks for {candlesticks.First().Symbol}");
            var result = CalculateTradeStrategies(candlesticks.First().Symbol, candlesticks.First().Interval.AsString(), candlesticks.Last().CloseTime);
            //log load algothrm is sucessful.

            //pass the results to the broker, the result indicated the percentage, 
            //likely hood of a profitable trade so may want to invest a higher amount.
            //
            //shouldn't really be doing it hear since this was just to ensure that all the indicatators can yield results.
        }

        public void ProcessLiveCandleStick(CandlestickEventArgs candlestickEventArgs)
        {
            _closePrices.Add(candlestickEventArgs.Candlestick.Close);
            Logger.LogInformation($"Processing Strategies for {candlestickEventArgs.Candlestick.Symbol} at {candlestickEventArgs.Candlestick.CloseTime:yyyy/MM/dd hh:mm}");
            var result = CalculateTradeStrategies(candlestickEventArgs.Candlestick.Symbol, candlestickEventArgs.Candlestick.Interval.AsString(), candlestickEventArgs.Candlestick.CloseTime);
            Logger.LogInformation($"Finished processing for Strategies for {candlestickEventArgs.Candlestick.Symbol} at {candlestickEventArgs.Candlestick.CloseTime:yyyy/MM/dd hh:mm}");
            if (result <= 0) return;
            var request = RequestBuilder.BuildTradeRequest(result/4, candlestickEventArgs.Candlestick.Symbol, candlestickEventArgs.Candlestick.Close, candlestickEventArgs.Candlestick.CloseTime);
            MessageBroker.Instance.Publish(this, request);
        }

        public double CalculateTradeStrategies(string symbol, string interval, DateTime closeTime)
        {
            double result = 0;

            //some strategied may required more than just the close price, particularly at higher timeframes, don't need dates assuming the ordered list will track that, so it might be worth converting it into a struct.
            //Logger.LogInformation($"Processing strategy {strategy}");
            tradingStrategies.Log($"Excel");
            tradingStrategies.Log($"Symbol: {symbol}");
            tradingStrategies.Log($"Interval: {interval}");
            tradingStrategies.Log($"CloseTime: {closeTime}");

            result = tradingStrategies.Calculate(_closePrices);
                //Logger.LogInformation($"Finished processing strategy {strategy} with result {result}");
            return result;
        }
    }
}
