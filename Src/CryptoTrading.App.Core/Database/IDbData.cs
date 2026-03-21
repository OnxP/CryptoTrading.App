using CryptoTrading.App.Core.Exchange;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Core.Database
{
    public interface IDbData
    {
        int LoadData(string sQL_STREAM_QUERY, DateTime currentTick, DateTime finalTick, List<string> symbol, int interval,int numberOfRows,int offSet);
        Dictionary<string, ExchangeCandlestick> GetData(DateTime currentTick, bool minute);
        IOrderedQueryable<CandleStickDb> GetQuerableData(DateTime currentTick, bool minute);
        List<ExchangeCandlestick> GetData(string symbol);
        bool CheckNextTick(DateTime nextTick, string symbol, bool minute);
        void ClearHistoric(DateTime from,bool minute);
        int Count(bool minute);
        void Initialise(DateTime from, DateTime to, List<string> symbols, CandleInterval interval);
    }
}
