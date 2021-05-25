using System;

namespace CryptoTrading.App.Core.Database
{
    public interface ICandleStickManagement
    {
        public DateTime CurrentTick { get; }
        public DateTime FinalTick { get; }

        void BuildTimeKeeper(DateTime from, DateTime dateTime);
        void AddMarketStream(Action invokeCandleStick);
        void AddStopLimitStream(Action invokeCandleStick);
        void RemoveStopLimitStream(Action invokeCandleStick);
        void StartTimeKeeper();
    }
}