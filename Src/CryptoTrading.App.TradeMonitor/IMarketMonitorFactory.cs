using CryptoTrading.App.Core.Trade;
using System.Threading.Tasks;

namespace CryptoTrading.App.Monitor
{
    public interface IMarketMonitorFactory
    {
        Task<ITradeMonitor> CreateMonitor(ITradeSignal signal);
    }
}
