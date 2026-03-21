using System;
using System.Threading;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core
{
    public interface IMarketData : IMarketDataEvents
    {
        DateTime From { get; set; }
        DateTime To { get; set; }
        public void Configure(IConfig request);
        ITaskController GetTaskController();
    }
}
