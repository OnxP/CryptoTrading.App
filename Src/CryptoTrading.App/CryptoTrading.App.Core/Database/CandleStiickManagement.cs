using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.Database
{
    public class DbCandleStickManagement : ICandleStickManagement
    {
        DateTime Start { get; set; }
        DateTime Finish { get; set; }
        Action _MarketDataStream { get; set; }
        Action _StopLimitMonitor { get; set; }

        Dictionary<int, DateTime> timeKeeper = new Dictionary<int, DateTime>();
        int _index = 0;

        public DateTime CurrentTick
        {
            get { 
                if (_index < timeKeeper.Count())
                {
                    return timeKeeper[_index];
                }
                else
                {
                    return FinalTick;
                }
            } 
        }

        public DateTime FinalTick
        {
            get { return timeKeeper.Last().Value; }
        }


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

        public static bool PauseFlow { get; set; } = false;

        public void StartTimeKeeper()
        {
            do
            {
                var task = new Task(_MarketDataStream);
                task.Start();
                task.Wait();

                if (_StopLimitMonitor != null)
                {
                    var monitorTask = new Task(_StopLimitMonitor);
                    monitorTask.Start();
                    monitorTask.Wait();
                }

                GetNextTick();
                if (_StopLimitMonitor==null) 
                    Thread.Sleep(10);

            } while (_index < timeKeeper.Count);
        }
    }
}
