using System;
using CryptoTrading.App.Core.Message_Broker;

namespace CryptoTrading.App.Core
{
    public interface IMessageBroker : IDisposable
    {
        void Publish<T>(object source, T message);
        void Subscribe<T>(Action<MessagePayload<T>> subscription);
        void Unsubscribe<T>(Action<MessagePayload<T>> subscription);
    }
}
