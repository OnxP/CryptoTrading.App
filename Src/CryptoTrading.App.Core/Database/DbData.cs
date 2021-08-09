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
        //public Dictionary<string, IEnumerable<IGrouping<DateTime, (Candlestick candlestick, int interval)>>> _data = 
        //    new Dictionary<string, IEnumerable<IGrouping<DateTime, (Candlestick candlestick, int interval)>>>();

        public Dictionary<DateTime, Dictionary<string, Candlestick>> data = new Dictionary<DateTime, Dictionary<string, Candlestick>>();

        IEnumerable<IGrouping<DateTime, (Candlestick candlestick, int interval)>> orderedList;

        public void LoadData(string sQL_STREAM_QUERY, DateTime currentTick, DateTime finalTick, string symbol, int interval)
        {
            lock (_lock)
            {
                //IEnumerable<CandleStickDb> orderedList;
                if (data.ContainsKey(currentTick) && data[currentTick].ContainsKey(symbol))
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

                        foreach (var candlestick in candleSticksToStream)
                        {
                            if(data.ContainsKey(candlestick.candlestick.CloseTime))
                            {
                                if (data[candlestick.candlestick.CloseTime].ContainsKey(symbol)) continue;

                                data[candlestick.candlestick.CloseTime].Add(symbol, candlestick.candlestick);
                            }
                            else
                            {
                                data.Add(candlestick.candlestick.CloseTime, new Dictionary<string, Candlestick>() { { symbol, candlestick.candlestick } });
                            }
                        }
                        //_data.Add(symbol, candleSticksToStream.OrderBy(x => x.candlestick.CloseTime).GroupBy(x => x.candlestick.CloseTime));
                    }
                }
            }
        }

        public Dictionary<string,Candlestick> GetData(DateTime currentTick)
        {
            lock (_lock)
            {
                Dictionary<string, Candlestick> list;
                if (data.TryGetValue(currentTick, out list))
                {
                    return list;
                }
                else
                {
                    return new Dictionary<string, Candlestick>();
                }

                //Dictionary<string,Candlestick> list = new Dictionary<string,Candlestick>();
                //foreach (var kvp in _data)
                //{
                //    //this is taking it time. slowing down the app. too much data to filter thorugh maybe use a dict list by close time
                //    list.Add(kvp.Key, kvp.Value.FirstOrDefault(x => x.Key == currentTick)?.FirstOrDefault().candlestick);
                //}
                //return list;
            }
        }
    }
}
