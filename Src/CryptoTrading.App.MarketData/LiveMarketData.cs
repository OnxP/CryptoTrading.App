using CryptoTrading.App.Core;
using CryptoTrading.App.Core.TradeRequest;
using System;
using System.Threading;
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

        public LiveMarketData(IBinanceApi api, ICandlestickClient candlestickClient, IBinanceWebSocketStream webSocket)
        {
            _api = api;
            _client = candlestickClient;
            _webSocket = webSocket;
            _webSocket.Message += (s, e) => _client.HandleMessage(e.Subject, e.Json);
        }
        public void Configure(IConfig request)
        {
            _webSocket.Uri = BinanceWebSocketStream.CreateUri(_client);
        }

        public ITaskController GetTaskController()
        {
            var controller = new RetryTaskController(_webSocket.StreamAsync);
            controller.Error += (s, e) => HandleError(e.Exception);
            return controller;
        }
    }
}
