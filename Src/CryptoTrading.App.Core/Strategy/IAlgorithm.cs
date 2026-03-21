using System.Collections.Generic;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core.Strategy
{
    public interface IAlgorithm
    {
        void Configure(IConfig config);
        void Subscribe(ExchangeSymbol symbol, IMarketDataEvents marketData);
    }
}
