using System;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.Database
{
    public interface ICandleStickManagement
    {
        public DateTime CurrentTick { get; }
        public DateTime FinalTick { get; }
        public DateTime NextTick { get; }
        public DateTime PreviousTick { get; }
        DateTime FirstTick { get; }
        int Index { get; }

        void BuildTimeKeeper(DateTime from, DateTime dateTime);
        void AddMarketStream(Func<Task> invokeCandleStick);
        Task StartTimeKeeper(CancellationToken cancellationToken);
        void AddStopLimitStream(Func<Task> invokeCandleStick);
        void RemoveStopLimitStream();
    }
}