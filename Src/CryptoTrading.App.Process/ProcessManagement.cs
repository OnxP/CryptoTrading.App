using System;
using System.Threading;
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
                do
                {
                    //Change the sleep count to time of day. 10 minutes past the hour...etc.
                    if (DateTime.Now.Minute % 2 == 0) LogProcess(Process.RefreshDatabaseConfig); //2 minutes
                    if (DateTime.Now.Minute == 25) LogProcess(Process.RefreshPositionsData); //1 Hour
                    if (DateTime.Now.Hour == 1 && DateTime.Now.Minute==10) // 24 Hours
                    {
                        LogProcess(Process.ArchiveAndReport); 
                        LogProcess(Process.RefreshSymbols);
                    }
                    Thread.Sleep(60 * 1000); //1 Minute
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
                LogProcess(Process.ReadDatabaseConfig);
                
                LogProcess(Process.BuildServiceObjects);

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
