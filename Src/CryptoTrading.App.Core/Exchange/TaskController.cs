using System;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.Exchange
{
    public class TaskController : ITaskController
    {
        private CancellationTokenSource _cts;
        private Task _task;
        private Func<CancellationToken, Task> _action;

        public TaskController() { }

        public TaskController(Func<CancellationToken, Task> action)
        {
            _action = action;
        }

        public bool IsActive => _task != null && !_task.IsCompleted;
        public Task Task => _task ?? Task.CompletedTask;

        public void Begin(Func<CancellationToken, Task> action = null)
        {
            _action = action ?? _action;
            _cts = new CancellationTokenSource();
            if (_action != null)
                _task = System.Threading.Tasks.Task.Run(() => _action(_cts.Token), _cts.Token);
        }

        public void Abort()
        {
            _cts?.Cancel();
        }

        public async Task CancelAsync()
        {
            Abort();
            if (_task != null)
            {
                try { await _task; }
                catch (OperationCanceledException) { }
            }
        }

        public async Task RestartAsync()
        {
            await CancelAsync();
            Begin();
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }

    public class RetryTaskController : TaskController
    {
    }
}
