using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.MarketData
{
    // Paper-trade variant of LiveMarketMonitor. Inherits the live subscribe
    // machinery (and so also goes through IExchangeProvider for stream data)
    // but short-circuits CheckOrder so simulated orders are always "filled".
    public class TestLiveMarketMonitor : LiveMarketMonitor
    {
        public TestLiveMarketMonitor(ILogger<LiveMarketMonitor> logger, IExchangeProvider exchange)
            : base(logger, exchange)
        {
        }

        public override Task<ExchangeOrder> CheckOrder(string orderId, string symbol)
        {
            var filledOrder = new ExchangeOrder
            {
                OrderId = orderId,
                Symbol = symbol,
                Status = ExchangeOrderStatus.Filled
            };
            return Task.FromResult(filledOrder);
        }
    }
}
