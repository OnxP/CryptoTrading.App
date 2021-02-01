using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace CryptoTrading.App.Core.Database
{
    public class DbCandleStickManagement : ICandleStickManagement
    {
        DateTime Start { get; set; }
        DateTime Finish { get; set; }

        Action _MarketDataStream { get; set; }
        List<Action> _StopLimitMonitor { get; set; } = new List<Action>();

        Dictionary<int, DateTime> timeKeeper = new Dictionary<int, DateTime>();
        int _index = 0;

        public DateTime CurrentTick
        {
            get { return timeKeeper[_index];} 
        }


        public void GetNextTick()
        {
            _index++;
            //return CurrentTick;
        }
        public void AddMarketData(IMarketData dbMarketMonitor)
        {
            throw new NotImplementedException();
        }

        public void AddMonitor(IMarketMonitor dbMarketMonitor)
        {
            throw new NotImplementedException();
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
            _StopLimitMonitor.Add(invokeCandleStick);
        }
        public void RemoveStopLimitStream(Action invokeCandleStick)
        {
            _StopLimitMonitor.Remove(invokeCandleStick);
        }

        public void StartTimeKeeper()
        {
            do
            {
                _MarketDataStream.Invoke();
                _StopLimitMonitor.ForEach(x => x.Invoke());
                GetNextTick();
                if (_StopLimitMonitor.Count != 0) Thread.Sleep(500);
            } while (_index < timeKeeper.Count);
        }
    }
}
