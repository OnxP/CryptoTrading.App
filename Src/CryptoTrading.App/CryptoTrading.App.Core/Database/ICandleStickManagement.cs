using System;

namespace CryptoTrading.App.Core.Database
{
    public interface ICandleStickManagement
    {
        public DateTime CurrentTick { get; }
        void BuildTimeKeeper(DateTime from, DateTime dateTime);
        void AddMarketStream(Action invokeCandleStick);
        void AddStopLimitStream(Action invokeCandleStick);
        void RemoveStopLimitStream(Action invokeCandleStick);
        void StartTimeKeeper();
    }
}