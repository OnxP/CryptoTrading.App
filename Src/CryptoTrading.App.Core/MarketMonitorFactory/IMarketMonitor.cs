using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Trade;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.MarketMonitorFactory
{
    /// <summary>
    /// Position-monitor surface for the trade monitor. All candle types are
    /// neutral (<see cref="ExchangeCandlestick"/> /
    /// <see cref="ExchangeCandlestickEvent"/>) so no bundled-SDK imports are
    /// forced on consumers.
    /// </summary>
    public interface IMarketMonitor
    {
        Task<bool> CheckOrder(ITransaction order);
        Task Subscribe(string symbol, string keyValue, Action<ExchangeCandlestickEvent> processCandleStick);
        bool IsSubscribed(string symbol, string keyValue);
        void UnSubscribe(string symbol, string keyValue);
        Task<List<ExchangeCandlestick>> GetHistoricCandleSticks(string symbol);
    }
}
