using Binance;
using Binance.Client;
using System.Collections.Generic;

namespace CryptoTrading.App.Core
{
    public interface IExecutionStrategy
    {
        IEntryStrategy EntryStrategy { get; set; }
        IExitStrategy ExitStrategy { get; set; }
        decimal Quantity {get;set;}
        decimal GetEntryPrice(CandlestickEventArgs candleStick);
        void LoadHistoricCandleSticks(List<Candlestick> candleSticks);
        StrategyState ProcessCandleStick(CandlestickEventArgs candleStick);
    }
}
