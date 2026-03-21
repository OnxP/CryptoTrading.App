using CryptoTrading.App.Core.Exchange;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.Database
{
    public interface IDbData
    {
        Task<int> LoadData(string sQL_STREAM_QUERY, DateTime currentTick, DateTime finalTick, List<string> symbol,
            int interval,int pageNumber);
        Dictionary<(string, CandleInterval), ExchangeCandlestick> GetData(DateTime currentTick);
        IOrderedQueryable<CandleStickDb> GetQuerableData(DateTime currentTick);
        List<ExchangeCandlestick> GetData(string symbol, CandleInterval interval);
        bool CheckNextTick(DateTime nextTick, string symbol, CandleInterval interval);
        void ClearHistoric(DateTime from);
        int Count();
        void Initialise(DateTime from, DateTime to, List<string> symbols, CandleInterval interval);
    }
}
