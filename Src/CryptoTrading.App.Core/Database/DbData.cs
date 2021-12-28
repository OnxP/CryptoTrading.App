using Binance;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Entity;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.Database
{
    public class DbData : IDbData
    {
        private static DbData _instance;
        public static DbData GetInstance => _instance ??= new DbData();

        private readonly object _lock = new object();

        private CryptoDbContext context = new CryptoDbContext();

        private IQueryable<CandleStickDb> data;
        public void Initialise(DateTime from, DateTime to, List<string> symbols, CandlestickInterval interval)
        {
            
            data = context.CandleSticks.Where(
                x=> x.CloseTime>=from && x.CloseTime<=to && symbols.Contains(x.Symbol) && (x.Interval == interval || x.Interval == CandlestickInterval.Minute)).AsNoTracking();
        }

        private Dictionary<DateTime, Dictionary<string, Candlestick>> _data = new Dictionary<DateTime, Dictionary<string, Candlestick>>();
        private Dictionary<DateTime, Dictionary<string, Candlestick>> _data_minute = new Dictionary<DateTime, Dictionary<string, Candlestick>>();

        public void LoadData(string sQL_STREAM_QUERY, DateTime currentTick, DateTime finalTick, List<string> symbols, int interval,int numberOfRows,int offSet)
        {
            lock (_lock)
            {
                Dictionary<DateTime, Dictionary<string, Candlestick>> data = interval == 0 ? _data_minute : _data;
                //if (data.ContainsKey(currentTick)) return;
                
                var query = sQL_STREAM_QUERY.Replace("@Symbols", Format(symbols));
                IEnumerable<CandleStickDb> candleSticks;


                    candleSticks = context.CandleSticks.SqlQuery(query,
                        currentTick, finalTick, interval, offSet,numberOfRows);
                    //Where(x =>
                    //symbols.Contains(x.Symbol) && (int)x.Interval == interval && x.CloseTime == currentTick &&
                    //(finalTick == null || x.CloseTime <= finalTick)).AsNoTracking();



                if (!candleSticks.Any()) throw new Exception("Bad Data.");
                    //candleSticks.All(x => candleSticksToStream.Add(CandleStickDb.ConvertObject(x)));

                    //first group by closetime
                    foreach (var candleStickList in candleSticks.GroupBy(x => x.CloseTime))
                    {
                        if (data.ContainsKey(candleStickList.Key))
                        {
                            foreach (var candleStick in candleStickList)
                            {
                                if (data[candleStickList.Key].ContainsKey(candleStick.Symbol)) continue;

                                data[candleStickList.Key].Add(candleStick.Symbol,
                                    CandleStickDb.ConvertObject(candleStick));
                            }
                        }
                        else
                        {
                            data.Add(
                                candleStickList
                                    .Key, //new Dictionary<string, Candlestick>() { { candlestick.Symbol, candlestick } });
                                candleStickList.ToDictionary(x => x.Symbol, CandleStickDb.ConvertObject));
                        }
                    }

            }
        }


        private string Format(List<string> symbols)
        {
            var str = string.Join("','", symbols);
            return str;
        }

        public Dictionary<string,Candlestick> GetData(DateTime currentTick,bool minute)
        {
            var data = minute ? _data_minute : _data;

            return data.TryGetValue(currentTick, out var listMinute)
                ? listMinute
                : new Dictionary<string, Candlestick>();
        }

        public IOrderedQueryable<CandleStickDb> GetQuerableData(DateTime currentTick, bool minute)
        {
            //var data = minute ? _data_minute : _data;

            //return data.TryGetValue(currentTick, out var listMinute)
            //    ? listMinute
            //    : new Dictionary<string, Candlestick>();

            return data.Where(x =>
                x.CloseTime == currentTick && minute
                    ? x.Interval == CandlestickInterval.Minute
                    : x.Interval != CandlestickInterval.Minute).OrderByDescending(x => x.Volume);
        }

        public bool CheckNextTick(DateTime nextTick, string symbol, bool minute)
        {
            var candlesticks = GetData(nextTick, minute);
            return candlesticks.Any() && candlesticks.ContainsKey(symbol);
        }

        public void RemoveTick(DateTime currentTick, bool minute)
        {
            var test = minute ? _data_minute.Remove(currentTick) : _data.Remove(currentTick);
        }

        public List<Candlestick> GetData(string symbol)
        {
            var list = new List<Candlestick>();
            foreach (var dict in _data)
            {
                if (dict.Value.TryGetValue(symbol, out var item))
                {
                    list.Add(item);
                }
            }

            return list;
        }

        public void ClearHistoric(DateTime from,bool minute)
        {
            var data = minute ? _data_minute : _data;   
            foreach (var kvp in data.Where(kvp => kvp.Key<=from))
            {
                RemoveTick(kvp.Key,minute);
            }
        }

        public int Count(bool minute)
        {
            var data = minute ? _data_minute : _data;
            return data.Count();
        }

        
    }
}
