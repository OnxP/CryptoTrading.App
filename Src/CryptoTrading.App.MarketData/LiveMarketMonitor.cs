using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Binance;
using Binance.Client;
using Binance.Utility;
using Binance.WebSocket;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.MarketData
{
    //Class monitors the position in the open trade and adjusts the stop loss, this could work on live streaming data 
    //Input(Initial) - Trade details.
    //Input(continuous) - CandleStick Processing.
    //StaticInput - Stop loss type and limit.
    //Output - Change Stop Limit Order

    //Processing logic
    //Initial - Configure Stoploss Monitor from Open trade. and set a stop limit order.
    //Continuous - Monitor price and once it hits a threshold reset stoploss to limit order X% below threshold then adjust threshold
    

    //this needs to send a signal back to Trade Processor with a ready. then the trade processor can decide on which order to execute the trades.
    public class LiveMarketMonitor : AbstractMarketData, IMarketMonitor
    {
        protected ICandlestickClient _client;
        protected IBinanceWebSocketStream _webSocket;
        protected IBinanceApi _api;
        protected IBinanceApiUser _user;
        public LiveMarketMonitor(ILogger<LiveMarketMonitor> logger,IBinanceApi api, ICandlestickClient candlestickClient, IBinanceWebSocketStream webSocket)
        {
            _api = api;
            _client = candlestickClient;
            _webSocket = webSocket;
            _webSocket.Message += (s, e) => _client.HandleMessage(e.Subject, e.Json);
            GetTaskController();
        }

        private List<string> symbols = new List<string>();

        protected LiveMarketMonitor()
        {
        }

        private ITaskController Controller { get; set; }
        public async virtual Task<bool> CheckOrder(ITransaction transaction)
        {
/* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            var newOrder = await _api.GetOrderAsync(_user, transaction.Pair, transaction.Order.ClientOrderId).ConfigureAwait(false);
            transaction.UpdateOrder(newOrder);
            return newOrder.Status == OrderStatus.Filled;
        }
        
        public void Subscribe(string symbol, string keyValue, Action<CandlestickEventArgs> processCandleStick)
        {
            symbols.Add(symbol);
            _client.Subscribe(symbol, CandlestickInterval.Minute, processCandleStick);
            Configure();
            if(!Controller.IsActive) Controller.Begin();
        }

        public void Configure()
        {
            _webSocket.Uri = BinanceWebSocketStream.CreateUri(_client);
        }

        public bool IsSubscribed(string symbol, string keyValue)
        {
            return symbols.Contains(symbol);
        }

        public void UnSubscribe(string symbol, string keyValue)
        {
            symbols.Remove(symbol);
            _client.Unsubscribe(symbol, CandlestickInterval.Minute);
        }

        public void GetTaskController()
        {
            Controller = new RetryTaskController(_webSocket.StreamAsync);
            Controller.Error += (s, e) => HandleError(e.Exception);
        }

        public override void Configure(IConfig request)
        {
            throw new NotImplementedException();
        }
    }
}