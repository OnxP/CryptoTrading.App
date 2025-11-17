using Binance;
using Binance.Client;
using System.Collections.Generic;

namespace CryptoTrading.App.Core
{
    public interface IExecutionStrategy
    {
        void LoadHistoricCandleSticks(List<Candlestick> candleSticks);
        StrategyState ProcessCandleStick(CandlestickEventArgs candleStick);
    }
}
