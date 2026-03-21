using CryptoTrading.App.Core.Exchange;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.MarketDataTesting
{
    public static class LiveStream
    {
        public static void StreamData()
        {
            // TODO: Rewrite to use IExchangeProvider.SubscribeCandlestickAsync() instead of old Binance WebSocket API
            throw new NotImplementedException("LiveStream needs to be rewritten to use IExchangeProvider");
        }
    }
}
