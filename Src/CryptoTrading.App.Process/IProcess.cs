using System.Threading;
using System.Threading.Tasks;
using Binance.Utility;

namespace CryptoTrading.App.Process
{
    public interface IProcess
    {
        void RefreshDatabaseConfig();
        void RefreshPositionsData();
        void ArchiveAndReport();
        void RefreshSymbols();
        void CompleteRunningTrades();
        void ReadDatabaseConfig();
        void BuildServiceObjects();
        void ReadBinanceData();
        bool IsRunning { get; }
        Task StartProcessing();
    }
}