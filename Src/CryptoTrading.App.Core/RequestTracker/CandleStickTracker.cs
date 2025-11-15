using System.Collections.Concurrent;
using System.Linq;
using Binance.Client;

namespace CryptoTrading.App.Core.RequestTracker
{
    public class CandleStickTracker
    {
        private static CandleStickTracker _instance;

        public static CandleStickTracker Instance
        {
            get
            {
                if (_instance == null) CandleSticks = new ConcurrentDictionary<string, CandlestickEventArgs>();
                return _instance ??= new CandleStickTracker();
            }
        }

        //store up the request here.
        public static ConcurrentDictionary<string, CandlestickEventArgs> CandleSticks 
        {
            get;
            set;
        }

        public bool IsFinal
        {
            get
            {
                var maxOpenDate = CandleSticks.Max(x => x.Value.Candlestick.OpenTime);

                return CandleSticks.Where(x => x.Value.Candlestick.OpenTime == maxOpenDate).Any(x => x.Value.IsFinal);
            }
        }

        public void UpdateCandleStick(CandlestickEventArgs candlestick)
        {
            if (CandleSticks.ContainsKey(candlestick.Candlestick.Symbol)) 
                CandleSticks.TryRemove(candlestick.Candlestick.Symbol, out _);
            CandleSticks.TryAdd(candlestick.Candlestick.Symbol, candlestick);
        }
    }
}
