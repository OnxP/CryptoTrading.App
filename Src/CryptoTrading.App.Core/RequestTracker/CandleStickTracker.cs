using System.Collections.Concurrent;
using System.Linq;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core.RequestTracker
{
    /// <summary>
    /// Per-symbol "latest candle event" cache used by the request-tracker
    /// flush path. PR 5c: retyped off the bundled <c>CandlestickEventArgs</c>
    /// onto the neutral <see cref="ExchangeCandlestickEvent"/>.
    /// </summary>
    public class CandleStickTracker
    {
        private static CandleStickTracker _instance;

        public static CandleStickTracker Instance
        {
            get
            {
                if (_instance == null) CandleSticks = new ConcurrentDictionary<string, ExchangeCandlestickEvent>();
                return _instance ??= new CandleStickTracker();
            }
        }

        public static decimal? GetClosePrice(string symbol)
        {
            if (CandleSticks.TryGetValue(symbol, out var candleStick))
            {
                return candleStick.Candlestick.Close;
            }
            return null;
        }

        //store up the request here.
        public static ConcurrentDictionary<string, ExchangeCandlestickEvent> CandleSticks
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

        public void UpdateCandleStick(ExchangeCandlestickEvent candlestick)
        {
            if (CandleSticks.ContainsKey(candlestick.Candlestick.Symbol))
                CandleSticks.TryRemove(candlestick.Candlestick.Symbol, out _);
            CandleSticks.TryAdd(candlestick.Candlestick.Symbol, candlestick);
        }
    }
}
