using Binance;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Drawing.Printing;
using System.Linq;
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
                x => x.CloseTime >= from && x.CloseTime <= to && symbols.Contains(x.Symbol) && (x.Interval == interval || x.Interval == CandlestickInterval.Minute)).AsNoTracking();
        }

        private Dictionary<DateTime, Dictionary<(string,CandlestickInterval), Candlestick>> _data = new Dictionary<DateTime, Dictionary<(string, CandlestickInterval), Candlestick>>();

        public async Task<int> LoadData(string sQL_STREAM_QUERY, DateTime currentTick, DateTime finalTick, List<string> symbols, 
            int interval,int pageNumber)
        {
            var query = context.CandleSticks
                .Where(p => p.CloseTime >= currentTick &&
                            (pageNumber == -1 ? p.CloseTime < finalTick : p.CloseTime <= finalTick) &&
                            p.Interval == (CandlestickInterval)interval &&
                            symbols.Contains(p.Symbol))
                .OrderBy(p => p.CloseTime)
                .AsNoTracking();

            var candleSticks = pageNumber == -1
                    ? await query.ToListAsync()
                    : await query.Skip(pageNumber * 20000).Take(20000).ToListAsync();


            if (!candleSticks.Any()) 
                throw new Exception(string.Format("Bad Data. {0} - {1} - {2} - {3} - {4}",query, currentTick, finalTick, interval, pageNumber));
            var count = candleSticks.Count();
            var grouping = candleSticks.GroupBy(x => x.CloseTime);

            foreach (var candleStickList in grouping)
            {
                if (_data.ContainsKey(candleStickList.Key))
                {
                    foreach (var candleStick in candleStickList)
                    {
                        if (_data[candleStickList.Key].ContainsKey((candleStick.Symbol,candleStick.Interval))) continue;

                        _data[candleStickList.Key].Add((candleStick.Symbol,candleStick.Interval),
                            CandleStickDb.ConvertObject(candleStick));
                    }
                }
                else
                {
                    _data.Add(candleStickList.Key,
                        candleStickList.ToDictionary(x => (x.Symbol,x.Interval), CandleStickDb.ConvertObject));
                }
            }
            return count;            
        }


        private string Format(List<string> symbols)
        {
            var str = string.Join("','", symbols);
            return str;
        }

        public Dictionary<(string,CandlestickInterval),Candlestick> GetData(DateTime currentTick)
        {
            return _data.TryGetValue(currentTick, out var listMinute)
                ? listMinute
                : new Dictionary<(string, CandlestickInterval), Candlestick>();
        }

        public IOrderedQueryable<CandleStickDb> GetQuerableData(DateTime currentTick)
        {
            return data.Where(x => x.CloseTime == currentTick).OrderByDescending(x => x.Volume);
        }

        public bool CheckNextTick(DateTime nextTick, string symbol, CandlestickInterval interval)
        {
            var candlesticks = GetData(nextTick);
            return candlesticks.Any() && candlesticks.ContainsKey((symbol,interval));
        }

        public void RemoveTick(DateTime currentTick)
        {
            var test = _data.Remove(currentTick);
        }

        public List<Candlestick> GetData(string symbol,CandlestickInterval interval)
        {
            var list = new List<Candlestick>();
            foreach (var dict in _data)
            {
                if (dict.Value.TryGetValue((symbol,interval), out var item))
                {
                    list.Add(item);
                }
            }

            return list;
        }

        public void ClearHistoric(DateTime from)
        {
            lock (_lock)
            {
                var keysToRemove = _data.Keys.Where(k => k <= from).ToList();
                foreach (var key in keysToRemove)
                {
                    _data.Remove(key);
                }
            }
        }

        public int Count()
        {
            return _data.Count();
        }

        
    }
}
