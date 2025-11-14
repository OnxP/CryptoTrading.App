using CryptoTrading.App.Core.Message_Broker;
using System;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core
{
    public interface IMessageBroker : IDisposable
    {
        Task Publish<T>(object source, T message);
        void Subscribe<T>(Func<MessagePayload<T>,Task> subscription);
        void Unsubscribe<T>(Func<MessagePayload<T>,Task> subscription);

        Task Publish<T>(string key,object source, T message);
        void Subscribe<T>(string key, Func<MessagePayload<T>,Task> subscription);
        void Unsubscribe<T>(string key, Func<MessagePayload<T>,Task> subscription);
    }
}
