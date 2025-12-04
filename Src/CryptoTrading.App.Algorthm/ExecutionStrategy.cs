using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using System.Collections.Generic;

namespace CryptoTrading.App.Algorithm
{
    public class ExecutionStrategy : IExecutionStrategy
    {
        public IEntryStrategy EntryStrategy { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
        public IExitStrategy ExitStrategy { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
        public decimal Quantity { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

        public decimal GetEntryPrice(CandlestickEventArgs candleStick)
        {
            throw new System.NotImplementedException();
        }

        public void LoadHistoricCandleSticks(List<Candlestick> candleSticks)
        {
            throw new System.NotImplementedException();
        }

        public StrategyState ProcessCandleStick(CandlestickEventArgs candleStick)
        {
            throw new System.NotImplementedException();
        }
    }
}
