using CryptoTrading.App.Core.Exchange;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;

namespace CryptoTrading.App.Core.Database
{
    public class DbData : IDbData
    {
        private static DbData _instance;
        public static DbData GetInstance => _instance ??= new DbData();

        private readonly object _lock = new object();

        private CryptoDbContext context = new CryptoDbContext();

        private IQueryable<CandleStickDb> data;
        public void Initialise(DateTime from, DateTime to, List<string> symbols, CandleInterval interval)
        {

            data = context.CandleSticks.Where(
                x=> x.CloseTime>=from && x.CloseTime<=to && symbols.Contains(x.Symbol) && (x.Interval == interval || x.Interval == CandleInterval.Minute_1)).AsNoTracking();
        }

        private Dictionary<DateTime, Dictionary<string, ExchangeCandlestick>> _data = new Dictionary<DateTime, Dictionary<string, ExchangeCandlestick>>();
        private Dictionary<DateTime, Dictionary<string, ExchangeCandlestick>> _data_minute = new Dictionary<DateTime, Dictionary<string, ExchangeCandlestick>>();

        public int LoadData(string sQL_STREAM_QUERY, DateTime currentTick, DateTime finalTick, List<string> symbols, int interval,int numberOfRows,int offSet)
        {
            lock (_lock)
            {
                Dictionary<DateTime, Dictionary<string, ExchangeCandlestick>> data = interval == 0 ? _data_minute : _data;
                //if (data.ContainsKey(currentTick)) return;

                var query = sQL_STREAM_QUERY.Replace("@Symbols", Format(symbols));
                var candleSticks = context.CandleSticks.SqlQuery(query,currentTick, finalTick, interval, offSet,numberOfRows);

                if (!candleSticks.Any()) throw new Exception("Bad Data.");
                var count = candleSticks.Count();
                var grouping = candleSticks.GroupBy(x => x.CloseTime);

                foreach (var candleStickList in grouping.OrderByDescending(x=>x.Key))
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
                                .Key, //new Dictionary<string, ExchangeCandlestick>() { { candlestick.Symbol, candlestick } });
                            candleStickList.ToDictionary(x => x.Symbol, CandleStickDb.ConvertObject));
                    }
                    //Check
                    //if(data[candleStickList.Key].Count() != symbols.Count) throw new Exception("Bad Data.");
                }
                return count;
            }
        }


        private string Format(List<string> symbols)
        {
            var str = string.Join("','", symbols);
            return str;
        }

        public Dictionary<string,ExchangeCandlestick> GetData(DateTime currentTick,bool minute)
        {
            var data = minute ? _data_minute : _data;

            return data.TryGetValue(currentTick, out var listMinute)
                ? listMinute
                : new Dictionary<string, ExchangeCandlestick>();
        }

        public IOrderedQueryable<CandleStickDb> GetQuerableData(DateTime currentTick, bool minute)
        {
            //var data = minute ? _data_minute : _data;

            //return data.TryGetValue(currentTick, out var listMinute)
            //    ? listMinute
            //    : new Dictionary<string, ExchangeCandlestick>();

            return data.Where(x =>
                x.CloseTime == currentTick && minute
                    ? x.Interval == CandleInterval.Minute_1
                    : x.Interval != CandleInterval.Minute_1).OrderByDescending(x => x.Volume);
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

        public List<ExchangeCandlestick> GetData(string symbol)
        {
            var list = new List<ExchangeCandlestick>();
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
            lock (_lock)
            {
                var data = minute ? _data_minute : _data;
                foreach (var kvp in data.Where(kvp => kvp.Key <= from))
                {
                    RemoveTick(kvp.Key, minute);
                }
            }
        }

        public int Count(bool minute)
        {
            var data = minute ? _data_minute : _data;
            return data.Count();
        }


    }
}
