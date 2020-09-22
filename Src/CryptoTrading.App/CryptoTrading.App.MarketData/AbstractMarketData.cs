using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;

namespace CryptoTrading.App.MarketData
{
    public abstract class AbstractMarketData
    {
        public abstract void Configure(IRequest request);

        protected static readonly object _sync = new object();

        protected static void HandleError(Exception e)
        {
            lock (_sync)
            {
                Console.WriteLine(e.Message);
            }
        }


        protected IDictionary<(string symbol, CandlestickInterval interval), Action<IEnumerable<Candlestick>>> _historicDataSubscribers = new Dictionary<(string symbol, CandlestickInterval interval), Action<IEnumerable<Candlestick>>>();
        protected IDictionary<(string symbol, CandlestickInterval interval), IList<Action<CandlestickEventArgs>>> _subscribers = new Dictionary<(string symbol, CandlestickInterval interval), IList<Action<CandlestickEventArgs>>>();
        //public events 

        public void InitialDataLoadSubscribe(string symbol, CandlestickInterval interval, Action<IEnumerable<Candlestick>> callback)
        {
            _historicDataSubscribers.Add((symbol, interval), callback);
        }
        public void InitialDataLoadUnSubscribe(string symbol, CandlestickInterval interval)
        {
            _historicDataSubscribers.Remove((symbol, interval));
        }

        public void InitialDataStreamSubscribe(string symbol, CandlestickInterval interval, Action<CandlestickEventArgs> callback)
        {
            if (!_subscribers.ContainsKey((symbol, interval)))
            {
                _subscribers.Add((symbol, interval), new List<Action<CandlestickEventArgs>>());
            }
            _subscribers[(symbol, interval)].Add(callback);
        }

        public void InitialDataStreamUnSubscribe(string symbol, CandlestickInterval interval, Action<CandlestickEventArgs> callback)
        {
            _subscribers.Remove((symbol, interval));
        }
    }
}
