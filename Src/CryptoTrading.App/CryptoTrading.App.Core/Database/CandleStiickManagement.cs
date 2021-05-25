using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace CryptoTrading.App.Core.Database
{
    public class DbCandleStickManagement : ICandleStickManagement
    {
        DateTime Start { get; set; }
        DateTime Finish { get; set; }

        private static readonly object _lock = new object();
        Action _MarketDataStream { get; set; }
        List<Action> _StopLimitMonitor { get; set; } = new List<Action>();
        List<Action> NextTickActions
        {
            get
            {
                lock (_lock)
                {
                    return _nextTickActions;
                }
            }
            set
            {
                lock (_lock)
                {
                    _nextTickActions = value;
                }
            }
        }

        List<Action> _nextTickActions = new List<Action>();

        Dictionary<int, DateTime> timeKeeper = new Dictionary<int, DateTime>();
        int _index = 0;

        public DateTime CurrentTick
        {
            get { return timeKeeper[_index];} 
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
            _nextTickActions.Add(new Action(() => _StopLimitMonitor.Add(invokeCandleStick)));
        }
        public void RemoveStopLimitStream(Action invokeCandleStick)
        {
            _nextTickActions.Add(new Action(()=> _StopLimitMonitor.Remove(invokeCandleStick)));
        }
        public static bool PauseFlow { get; set; } = false;

        public void StartTimeKeeper()
        {
            do
            {
                _MarketDataStream.Invoke();
                _StopLimitMonitor.ForEach(x => x.Invoke());

                GetNextTick();
                if (NextTickActions.Count > 0)
                {
                    NextTickActions.ForEach(x => x.Invoke());
                    NextTickActions.Clear();
                }
                if (_StopLimitMonitor.Count == 0) 
                    Thread.Sleep(10);
            } while (_index < timeKeeper.Count);
        }
    }
}
