using System;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.Exchange
{
    public interface ITaskController : IDisposable
    {
        bool IsActive { get; }
        Task Task { get; }
        void Begin(Func<CancellationToken, Task> action = null);
        void Abort();
        Task CancelAsync();
        Task RestartAsync();
    }
}
