using System.Collections.Generic;
using Binance;
using Binance.Client;

namespace CryptoTrading.App.Core
{
    public interface IAlgorithm
    {
        void Configure(IConfig config);
        void Subscribe(Symbol symbol, IMarketDataEvents marketData);
    }
}
