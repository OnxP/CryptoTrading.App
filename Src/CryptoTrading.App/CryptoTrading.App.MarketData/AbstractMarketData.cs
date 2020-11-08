using Binance;
using Binance.Client;
using CryptoTrading.App.Core.TradeRequest;
using System;
using System.Collections.Generic;

namespace CryptoTrading.App.MarketData
{
    public abstract class AbstractMarketData : IMarketDataEvents
    {
        public abstract void Configure(IRequest request);

        protected static readonly object Sync = new object();

        protected static void HandleError(Exception e)
        {
            lock (Sync)
            {
                Console.WriteLine(e.Message);
            }
        }


        protected IDictionary<(string symbol, CandlestickInterval interval), Action<IEnumerable<Candlestick>>> historicDataSubscribers = new Dictionary<(string symbol, CandlestickInterval interval), Action<IEnumerable<Candlestick>>>();
        protected IDictionary<(string symbol, CandlestickInterval interval), IList<Action<CandlestickEventArgs>>> subscribers = new Dictionary<(string symbol, CandlestickInterval interval), IList<Action<CandlestickEventArgs>>>();
        //public events 

        public void InitialDataLoadSubscribe(string symbol, CandlestickInterval interval, Action<IEnumerable<Candlestick>> callback)
        {
            historicDataSubscribers.Add((symbol, interval), callback);
        }
        public void InitialDataLoadUnSubscribe(string symbol, CandlestickInterval interval)
        {
            historicDataSubscribers.Remove((symbol, interval));
        }

        public void InitialDataStreamSubscribe(string symbol, CandlestickInterval interval, Action<CandlestickEventArgs> callback)
        {
            if (!subscribers.ContainsKey((symbol, interval)))
            {
                subscribers.Add((symbol, interval), new List<Action<CandlestickEventArgs>>());
            }
            subscribers[(symbol, interval)].Add(callback);
        }

        public void InitialDataStreamUnSubscribe(string symbol, CandlestickInterval interval, Action<CandlestickEventArgs> callback)
        {
            subscribers.Remove((symbol, interval));
        }
    }
}
