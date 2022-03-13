using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Binance.Utility;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Process
{
    public class ProcessManagement: IProcessManagement
    {
        //configuration 
        //set up configuration for the process. the main one is run type. Algorithm testing, backtesting, market testing and live.

        //specific config should come from the database
        public ProcessManagement(ILogger<ProcessManagement> logger, IProcess process)
        {
            Logger = logger;
            Process = process;
        }
        private ILogger Logger { get; }
        public IProcess Process { get; }

        public void Run(int retries)
        {
            int i = 0;
            while (i <= retries)
            {
                var res = Run().Result;
                if (res == 0) return;
                i++;
                Logger.LogError($"App Failed Retrying attempt {i}");
            }
        }

        public async Task<int> Run()
        {
            if (!InitiliseProcess())
            {
                Logger.LogError($"Initilisation failed. Existing App..");
            }

            try
            {

                var task = LoopProcess().ConfigureAwait(true);
                //loops over the database checks for updates every 2 minutes.
                await Process.StartProcessing();

                LogProcess(Process.CompleteRunningTrades);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Logger.LogError($"Error running process {e.Message} - {e.StackTrace}");
                return -1;
            }

            return 0;
        }

        private async Task LoopProcess()
        {
            await Task.Delay(1000);
            while (true)
            {
                if (!Process.IsRunning) break;
                //Change the sleep count to time of day. 10 minutes past the hour...etc.
                if (DateTime.Now.Minute % 2 == 0) LogProcess(Process.RefreshDatabaseConfig); //2 minutes
                if (DateTime.Now.Minute == 25) LogProcess(Process.RefreshPositionsData); //1 Hour
                if (DateTime.Now.Hour == 1 && DateTime.Now.Minute == 10) // 24 Hours
                {
                    LogProcess(Process.ArchiveAndReport);
                    LogProcess(Process.RefreshSymbols);
                }
                await Task.Delay(60 * 1000); //1 Minute
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
            var timer = new Stopwatch();
            timer.Start();
            Logger.LogInformation($"Executing Function: {function.Method.Name} Start: {DateTime.Now}");
            function.Invoke();
            timer.Stop();
            Logger.LogInformation($"Completed Function: {function.Method.Name} Finish: {DateTime.Now} - {timer.ElapsedMilliseconds}ms");
        }

    }
}
