using CryptoTrading.App.Core;
using CryptoTrading.App.Core.TradeRequest;
using System;

namespace CryptoTrading.App.MarketData
{
    public class LiveMarketData : IMarketData
    {
        public DateTime From { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public void Configure(IRequest request)
        {
            throw new NotImplementedException();
        }

        public void StartStream()
        {
            throw new NotImplementedException();
        }
    }
}
