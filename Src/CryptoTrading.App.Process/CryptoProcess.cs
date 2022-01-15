using System;
using System.Collections.Generic;
using System.Text;
using CryptoTrading.App.Algorithm;
using CryptoTrading.App.Core;

namespace CryptoTrading.App.Process
{
    public class CryptoProcess
    {
        public CryptoProcess(IMarketData marketData, ITradeProcessor tradeProcessor, IAlgorithm algorithm)  
        {

        }
        public void ReadBinanceData()
        {
            throw new NotImplementedException();
        }

        public void ReadDatabaseConfig()
        {
            throw new NotImplementedException();
        }

        public void ArchiveAndReport()
        {
            throw new NotImplementedException();
        }

        public void StartProcessing()
        {
            throw new NotImplementedException();
        }

        public void BuildServiceObjects()
        {
            throw new NotImplementedException();
        }

        public void CompleteRunningTrades()
        {
            throw new NotImplementedException();
        }

        public void RefreshBinanceData()
        {
            throw new NotImplementedException();
        }

        public void RefreshDatabaseConfig()
        {
            throw new NotImplementedException();
        }

        public bool IsRunning { get; set; }
    }
}
