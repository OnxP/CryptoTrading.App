using System;
using System.Threading;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Process
{
    public class ProcessManagement
    {
        //configuration 
        //set up configuration for the process. the main one is run type. Algorithm testing, backtesting, market testing and live.

        //specific config should come from the database
        public ProcessManagement(ILogger logger, CryptoProcess process)
        {
            Logger = logger;
            Process = process;
        }
        private ILogger Logger { get; }
        public CryptoProcess Process { get; }

        public void Run()
        {
            if (!InitiliseProcess())
            {
                Logger.LogError($"Initilisation failed. Existing App..");
            }

            try
            {
                LogProcess(Process.StartProcessing);
                //loops over the database checks for updates every 2 minutes.
                int loopCount = 0;
                do
                {
                    //Change the sleep count to time of day. 10 minutes past the hour...etc.
                    if (loopCount % 2 == 0) LogProcess(Process.RefreshDatabaseConfig); //2 minutes
                    if (loopCount % 60 == 0) LogProcess(Process.RefreshBinanceData); //1 Hour
                    if (loopCount % 1440 == 0) 
                    {
                        LogProcess(Process.ArchiveAndReport); // 24 Hours
                        loopCount = 0;
                    }
                    Thread.Sleep(60 * 1000); //1 Minute
                    loopCount++;
                } while (Process.IsRunning);

                LogProcess(Process.CompleteRunningTrades);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Logger.LogError($"Error running process {e.Message} - {e.StackTrace}");
            }
        }

        private bool InitiliseProcess()
        {
            try
            {
                LogProcess(Process.BuildServiceObjects);

                LogProcess(Process.ReadDatabaseConfig);

                LogProcess(Process.ReadBinanceData);

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Logger.LogError($"Error running process {e.Message} - {e.StackTrace}");

                return false;
            }
        }

        private void LogProcess(Action function)
        {
            Logger.LogInformation($"");
            function.Invoke();
            Logger.LogInformation($"");
        }

    }
}
