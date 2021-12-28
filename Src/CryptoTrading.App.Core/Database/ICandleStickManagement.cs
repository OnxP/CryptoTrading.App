using System;

namespace CryptoTrading.App.Core.Database
{
    public interface ICandleStickManagement
    {
        public DateTime CurrentTick { get; }
        public DateTime FinalTick { get; }
        public DateTime NextTick { get; }
        DateTime FirstTick { get; }
        int Index { get; }

        void BuildTimeKeeper(DateTime from, DateTime dateTime);
        void AddMarketStream(Action invokeCandleStick);
        void StartTimeKeeper();
        void AddStopLimitStream(Action invokeCandleStick);
        void RemoveStopLimitStream();
    }
}