namespace CryptoTrading.App.Process
{
    public interface IProcess
    {
        bool IsRunning { get; }
        void StartProcessing();
        void RefreshDatabaseConfig();
        void RefreshPositionsData();
        void ArchiveAndReport();
        void RefreshSymbols();
        void CompleteRunningTrades();
        void ReadDatabaseConfig();
        void BuildServiceObjects();
        void ReadBinanceData();
    }
}