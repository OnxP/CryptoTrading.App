using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Trade;
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

        public override async Task<bool> CheckOrder(ITransaction transaction)
        {
            transaction.Complete();
            await Task.CompletedTask;
            return true;
        }
    }
}
