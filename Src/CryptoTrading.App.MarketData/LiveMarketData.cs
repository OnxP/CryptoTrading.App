using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.TradeRequest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CryptoTrading.App.Core.RequestTracker;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.MarketData
{
    public class LiveMarketData : AbstractMarketData, IMarketData
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        IExchangeProvider _provider;
        public ILogger<LiveMarketData> Logger { get; set; }
        public bool loadHistoric = true;

        public LiveMarketData(ILogger<LiveMarketData> logger, IExchangeProvider provider)
        {
            Logger = logger;
            _provider = provider;
        }

        public override void Configure(IConfig request)
        {
            if (loadHistoric)
            {
                LoadHistoricCandleSticks(request);
                loadHistoric = false;
            }
            _provider.UnsubscribeAllAsync().Wait();
            foreach (var subscriber in subscribers)
            {
                _provider.SubscribeCandlestickAsync(subscriber.Key.symbol, subscriber.Key.interval, candle =>
                {
                    var evt = new ExchangeCandlestickEvent
                    {
                        EventTime = DateTime.UtcNow,
                        Candlestick = candle,
                        FirstTradeId = 0,
                        LastTradeId = 0,
                        IsFinal = true
                    };
                    subscriber.Value.First()(evt);
                }).Wait();
            }
        }

        public ITaskController GetTaskController()
        {
            // The provider handles streaming internally; return a simple task controller
            throw new NotImplementedException("WebSocket streaming is now managed by IExchangeProvider");
        }

        public void LoadHistoricCandleSticks(IConfig config)
        {
            Logger.LogInformation("Loading Historic Candlesticks");
            var tasks = new List<Task>();
            foreach (var item in historicDataSubscribers)
            {
                tasks.Add(LoadHistoricData(_provider, item.Key, config.NumberOfCandleSticksToLoad, item.Value));
            }

            Task.WaitAll(tasks.ToArray());
            Logger.LogInformation("Finished loading Historic Candlesticks");
            tasks = new List<Task>();
            //LoadData

            Logger.LogInformation("Loading Candlesticks");
        }

        private async Task LoadHistoricData(IExchangeProvider provider, (string symbol, CandleInterval interval) symbol, int numberOfCandleSticks, IList<Action<IEnumerable<ExchangeCandlestick>>> callback)
        {
            var calculatedFrom = CandleStickIntervalHelper.CalculateCandleStickTimeFrom(DateTime.Now, symbol.interval, numberOfCandleSticks).ToUniversalTime();
            var candleSticks = await provider.GetCandlesticksAsync(symbol.symbol, symbol.interval, calculatedFrom, DateTime.Now.ToUniversalTime());
            //need to drop first candle
            var sticks = candleSticks.Reverse();
            foreach (var action in callback)
            {
                action.Invoke(sticks);
            }
        }
    }
}
