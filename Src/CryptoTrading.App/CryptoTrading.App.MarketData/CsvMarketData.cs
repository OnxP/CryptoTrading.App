using CryptoTrading.App.Core;
using CryptoTrading.App.Core.TradeRequest;
using System;

namespace CryptoTrading.App.MarketData
{
    public class CsvMarketData : AbstractMarketData, IMarketData
    {
        public DateTime From { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override void Configure(IRequest request)
        {

        }

        public void StartStream()
        {
            throw new NotImplementedException();
        }
    }
}
