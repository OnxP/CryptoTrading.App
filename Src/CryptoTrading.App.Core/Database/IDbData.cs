using Binance;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Core.Database
{
    public interface IDbData
    {
        void LoadData(string sQL_STREAM_QUERY, DateTime currentTick, DateTime finalTick, List<string> symbol, int interval,int numberOfRows,int offSet);
        Dictionary<string, Candlestick> GetData(DateTime currentTick, bool minute);
        IOrderedQueryable<CandleStickDb> GetQuerableData(DateTime currentTick, bool minute);
        List<Candlestick> GetData(string symbol);
        bool CheckNextTick(DateTime nextTick, string symbol, bool minute);
        void ClearHistoric(DateTime from,bool minute);
        int Count(bool minute);
        void Initialise(DateTime from, DateTime to, List<string> symbols, CandlestickInterval interval);
    }
}
