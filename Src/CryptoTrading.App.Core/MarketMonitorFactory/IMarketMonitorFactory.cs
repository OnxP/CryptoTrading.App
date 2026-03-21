using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.MarketMonitorFactory
{
    public interface IMarketMonitorFactory
    {
        Task<ITradeMonitor>CreateMonitor(ITradeRequest what, IPositions positions);
    }
}