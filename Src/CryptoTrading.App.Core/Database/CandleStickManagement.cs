using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Core.Database
{
    public class DbCandleStickManagement : ICandleStickManagement
    {
        public DbCandleStickManagement(ILogger<ICandleStickManagement> logger)
        {
            Logger = logger;
        }

        public ILogger<ICandleStickManagement> Logger { get; }
        DateTime Start { get; set; }
        DateTime Finish { get; set; }
        Action _MarketDataStream { get; set; }
        Action _StopLimitMonitor { get; set; }

        Dictionary<int, DateTime> timeKeeper = new Dictionary<int, DateTime>();
        int _index = 0;
        public static bool PauseFlow { get; set; } = false;

        public DateTime CurrentTick => _index < timeKeeper.Count() ? timeKeeper[_index] : FinalTick;

        public DateTime NextTick => _index < timeKeeper.Count() ? timeKeeper[_index + 1] : FinalTick;

        public DateTime FinalTick => timeKeeper.Last().Value;

        public DateTime FirstTick => timeKeeper[0];
        public int Index => _index;

        public void GetNextTick()
        {
            _index++;
            //return CurrentTick;
        }
        public void BuildTimeKeeper (DateTime From, DateTime Finish)
        {
            int i = 0;
            var currentTime = From;
            while (currentTime < Finish)
            {
                currentTime = From.AddMinutes(i);
                timeKeeper.Add(i++,currentTime);
            }
            //GetNextTick();
        }

        public void AddMarketStream(Action invokeCandleStick)
        {
            _MarketDataStream = invokeCandleStick;
        }
        public void AddStopLimitStream(Action invokeCandleStick)
        {
            _StopLimitMonitor = invokeCandleStick;
        }
        public void RemoveStopLimitStream()
        {
            _StopLimitMonitor = null;
        }

        

        public void StartTimeKeeper()
        {
            do
            {
                Logger.LogDebug($"Processing tick :{CurrentTick}");
                _MarketDataStream.Invoke();
                //var task = new Task(_MarketDataStream);
                //task.Start();
                //task.Wait();

                if (_StopLimitMonitor != null)
                {
                    _StopLimitMonitor.Invoke();
                    //var monitorTask = new Task(_StopLimitMonitor);
                    //monitorTask.Start();
                    //monitorTask.Wait();
                }
                Logger.LogDebug($"Finished Processing tick :{CurrentTick}");

                GetNextTick();
                //if (_StopLimitMonitor==null) 
                //    Thread.Sleep(10);

            } while (_index < timeKeeper.Count-1);
        }
    }
}
