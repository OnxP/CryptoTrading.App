using CryptoTrading.App.Core;
using CryptoTrading.App.Core.TradeRequest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Binance;
using Binance.Client;
using Binance.Utility;
using Binance.WebSocket;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.MarketData
{
    public class LiveMarketData : AbstractMarketData, IMarketData
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        readonly ICandlestickClient _client;
        readonly IBinanceWebSocketStream _webSocket;
        IBinanceApi _api;
        public ILogger<LiveMarketData> Logger { get; set; }
        public bool loadHistoric = true;

        public LiveMarketData(ILogger<LiveMarketData> logger, IBinanceApi api, ICandlestickClient candlestickClient, IBinanceWebSocketStream webSocket)
        {
            Logger = logger;
            _api = api;
            _client = candlestickClient;
            _webSocket = webSocket;
            _webSocket.Message += (s, e) => _client.HandleMessage(e.Subject, e.Json);
        }

        public void Configure(IConfig request)
        {
            if (loadHistoric)
            {
                LoadHistoricCandleSticks(request);
                loadHistoric = false;
            }
            _client.Unsubscribe();
            foreach (var subscriber in subscribers)
            {
                _client.Subscribe(subscriber.Key.symbol, subscriber.Key.interval, subscriber.Value.First());
            }

            _webSocket.Uri = BinanceWebSocketStream.CreateUri(_client);
        }

        public ITaskController GetTaskController()
        {
            var controller = new RetryTaskController(_webSocket.StreamAsync);
            controller.Error += (s, e) => HandleError(e.Exception);
            return controller;
        }

        public void LoadHistoricCandleSticks(IConfig config)
        {
            Logger.LogInformation("Loading Historic Candlesticks");
            var tasks = new List<Task>();
            foreach (var item in historicDataSubscribers)
            {
                tasks.Add(LoadHistoricData(_api, item.Key, config.NumberOfCandleSticksToLoad, item.Value));
            }

            Task.WaitAll(tasks.ToArray());
            Logger.LogInformation("Finished loading Historic Candlesticks");
            tasks = new List<Task>();
            //LoadData

            Logger.LogInformation("Loading Candlesticks");
        }

        private async System.Threading.Tasks.Task LoadHistoricData(IBinanceApi api, (string symbol, CandlestickInterval interval) symbol, int numberOfCandleSticks, IList<Action<IEnumerable<Candlestick>>> callback)
        {
            var calculatedFrom = CandleStickIntervalHelper.CalculateCandleStickTimeFrom(DateTime.Now, symbol.interval, numberOfCandleSticks).ToUniversalTime();
            var candleSticks = await api.GetCandlesticksAsync(symbol.symbol, symbol.interval, 0, calculatedFrom, DateTime.Now.ToUniversalTime());
            //need to drop first candle
            var sticks = candleSticks.Reverse();
            foreach (var action in callback)
            {
                action.Invoke(sticks);
            }

        }
    }
}