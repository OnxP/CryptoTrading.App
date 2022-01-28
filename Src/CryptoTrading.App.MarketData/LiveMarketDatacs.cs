using CryptoTrading.App.Core;
using CryptoTrading.App.Core.TradeRequest;
using System;
using CryptoTrading.App.Process;

namespace CryptoTrading.App.MarketData
{
    public class LiveMarketData : AbstractMarketData, IMarketData
    {
        public DateTime From { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public DateTime To { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public void Configure(IConfig request)
        {
            throw new NotImplementedException();
        }

        public override void Configure(IRequest request)
        {
            throw new NotImplementedException();
        }

        public void StartStream()
        {
            throw new NotImplementedException();
        }
    }
}
