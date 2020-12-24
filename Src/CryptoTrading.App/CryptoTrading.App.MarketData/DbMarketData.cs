using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.TradeRequest;
using System;

namespace CryptoTrading.App.MarketData
{
    public class DbMarketData : AbstractMarketData, IMarketData
    {
        public DateTime From { get; set; }

        CryptoDBContext context;

        public override void Configure(IRequest request)
        {
            context = new CryptoDBContext();
        }

        public void StartStream()
        {
            throw new NotImplementedException();
        }
    }
}
