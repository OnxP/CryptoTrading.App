using Binance;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CryptoTrading.App.Core.Database
{
    public class DbData : IDbData
    {
        private static DbData _instance;
        public static DbData GetInstance
        {
            get
            {
                if (_instance == null) _instance = new DbData();
                return _instance;
            }
        }
        private readonly object _lock = new object();
        public Dictionary<string, IEnumerable<IGrouping<DateTime, (Candlestick candlestick, int interval)>>> _data = 
            new Dictionary<string, IEnumerable<IGrouping<DateTime, (Candlestick candlestick, int interval)>>>();

        IEnumerable<IGrouping<DateTime, (Candlestick candlestick, int interval)>> orderedList;

        public void LoadData(string sQL_STREAM_QUERY, DateTime currentTick, DateTime finalTick, string symbol, int interval)
        {
            lock (_lock)
            {
                //IEnumerable<CandleStickDb> orderedList;
                if (_data.ContainsKey(symbol))
                {
                    return;
                }
                else
                {
                    using (var context = new CryptoDBContext())
                    {
                        List<(Candlestick candlestick, int interval)> candleSticksToStream = new List<(Candlestick candlestick, int interval)>();
                        var candleSticks = context.CandleSticks.SqlQuery(sQL_STREAM_QUERY, currentTick, finalTick, symbol, interval).ToList();
                        if (candleSticks.Count() == 0) throw new Exception("Bad Data.");
                        candleSticks.ForEach(x => candleSticksToStream.Add((CandleStickDb.ConvertObject(x), interval)));

                        _data.Add(symbol, candleSticksToStream.OrderBy(x => x.candlestick.CloseTime).GroupBy(x => x.candlestick.CloseTime));
                    }
                }
            }
        }

        public Dictionary<string,Candlestick> GetData(DateTime currentTick)
        {
            lock (_lock)
            {
                Dictionary<string,Candlestick> list = new Dictionary<string,Candlestick>();
                foreach (var kvp in _data)
                {
                    list.Add(kvp.Key, kvp.Value.FirstOrDefault(x => x.Key == currentTick)?.FirstOrDefault().candlestick);
                }
                return list;
            }
        }
    }
}
