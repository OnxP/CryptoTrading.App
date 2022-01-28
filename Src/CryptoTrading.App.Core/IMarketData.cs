using CryptoTrading.App.Core.TradeRequest;
using System;
using CryptoTrading.App.Process;

namespace CryptoTrading.App.Core
{
    public interface IMarketData : IMarketDataEvents
    {
        DateTime From { get; set; }
        DateTime To { get; set; }
        public void Configure(IConfig request);
        void StartStream();
    }
}
