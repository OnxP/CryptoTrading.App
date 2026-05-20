using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.RequestTracker
{
    public class RequestTracker
    {
        private static RequestTracker _instance;

        public static RequestTracker Instance
        {
            get
            {
                if (_instance == null) Signals = new ConcurrentDictionary<string, (string KeyValue, ITradeSignal Signal)>();
                return _instance ??= new RequestTracker();
            }
        }

        public static ConcurrentDictionary<string, (string KeyValue, ITradeSignal Signal)> Signals { get; set; }

        public void Add(string symbol, ITradeSignal signal, string keyValue)
        {
            Signals.AddOrUpdate(
                symbol,
                (keyValue, signal),
                (key, existing) => (keyValue, signal));
        }

        public async Task SubmitRequests()
        {
            if (Signals.Any())
            {
                await Task.WhenAll(
                    Signals.Select(r =>
                        MessageBroker.Instance.Publish(
                            r.Value.KeyValue,
                            this,
                            r.Value.Signal)));

                Signals.Clear();
            }
        }
    }
}
