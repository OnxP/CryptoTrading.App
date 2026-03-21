using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;

namespace CryptoTrading.App.Core.RequestTracker
{
    public class RequestTracker
    {
        private static RequestTracker _instance;

        public static RequestTracker Instance
        {
            get
            {
                if (_instance == null) Requests = new ConcurrentDictionary<string, Tuple<string, ITradeRequest>>();
                return _instance ??= new RequestTracker();
            }
        }

        private readonly object _lock = new object();
        //store up the request here.
        public static ConcurrentDictionary<string,Tuple<string,ITradeRequest>> Requests 
        {
            get;
            set;
        }
        public void Add(string symbol, ITradeRequest request, string keyValue)
        {
            Requests.TryAdd(symbol, new Tuple<string, ITradeRequest>(keyValue, request));

            //if (CandleStickTracker.Instance.IsFinal) ProcessRequests();
        }

        private void ProcessRequests()
        {
            lock (_lock)
            {
                if (!Requests.Any()) return;

                var order = Requests.OrderByDescending(x => x.Value.Item2.Volume);

                foreach (var request in order)
                {
                    MessageBroker.Instance.Publish(request.Value.Item1,null,request.Value.Item2);
                }

                Requests.Clear();
            }
        }

        public void SubmitRequests()
        {
            if (!Requests.Any()) return;

            var req = Requests.OrderByDescending(x => x.Value.Item2.Volume);

            foreach (var request in req)
            {
                MessageBroker.Instance.Publish(request.Value.Item1, this, request.Value.Item2);
            }

            Requests.Clear();
        }
    }
}
