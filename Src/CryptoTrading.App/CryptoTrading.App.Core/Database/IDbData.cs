using Binance;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CryptoTrading.App.Core.Database
{
    public interface IDbData
    {
        void LoadData(string sQL_STREAM_QUERY, DateTime currentTick, DateTime finalTick, string symbol, int interval);
        IGrouping<DateTime, (Candlestick candlestick, int interval)> GetData(string symbol, DateTime currentTick);
    }
}
