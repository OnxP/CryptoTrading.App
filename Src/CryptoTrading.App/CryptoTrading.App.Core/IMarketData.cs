using CryptoTrading.App.Core.TradeRequest;
using System;

namespace CryptoTrading.App.Core
{
    public interface IMarketData
    {
        DateTime From { get; set; }
        public void Configure(IRequest request);
        void StartStream();
    }
}
