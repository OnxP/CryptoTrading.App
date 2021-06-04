using Binance;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core
{
    namespace Message_Broker
    {
        public class MessageBroker : IMessageBroker
        {
            private static MessageBroker _instance;
            private readonly Dictionary<Type, List<Delegate>> _subscribers;
            private readonly Dictionary<(string,Type), List<Delegate>> _keySubscribers;
            public static MessageBroker Instance
            {
                get
                {
                    if (_instance == null)
                        _instance = new MessageBroker();
                    return _instance;
                }
            }

            private MessageBroker()
            {
                _subscribers = new Dictionary<Type, List<Delegate>>();
                _keySubscribers = new Dictionary<(string,Type), List<Delegate>>();
            }

            public void Publish<T>(object source, T message)
            {
                if (message == null || source == null)
                    return;
                if (!_subscribers.ContainsKey(typeof(T)))
                {
                    return;
                }
                var delegates = _subscribers[typeof(T)];
                if (delegates == null || delegates.Count == 0) return;
                var payload = new MessagePayload<T>(message, source);
                foreach (var handler in delegates.Select
                (item => item as Action<MessagePayload<T>>))
                {
                    Task.Factory.StartNew(() => handler?.Invoke(payload));
                }
            }

            public void Subscribe<T>(Action<MessagePayload<T>> subscription)
            {
                var delegates = _subscribers.ContainsKey(typeof(T)) ?
                                _subscribers[typeof(T)] : new List<Delegate>();
                if (!delegates.Contains(subscription))
                {
                    delegates.Add(subscription);
                }
                _subscribers[typeof(T)] = delegates;
            }

            public void Unsubscribe<T>(Action<MessagePayload<T>> subscription)
            {
                if (!_subscribers.ContainsKey(typeof(T))) return;
                var delegates = _subscribers[typeof(T)];
                if (delegates.Contains(subscription))
                    delegates.Remove(subscription);
                if (delegates.Count == 0)
                    _subscribers.Remove(typeof(T));
            }

            public void Publish<T>(string keyValue, object source, T message)
            {
                if (message == null || source == null)
                    return;
                if (!_keySubscribers.ContainsKey((keyValue,typeof(T))))
                {
                    return;
                }
                var delegates = _keySubscribers[(keyValue,typeof(T))];
                if (delegates == null || delegates.Count == 0) return;
                var payload = new MessagePayload<T>(message, source);
                foreach (var handler in delegates.Select
                (item => item as Action<MessagePayload<T>>))
                {
                    Task.Factory.StartNew(() => handler?.Invoke(payload));
                }
            }

            public void Subscribe<T>(string keyValue, Action<MessagePayload<T>> subscription)
            {
                var delegates = _keySubscribers.ContainsKey((keyValue, typeof(T))) ?
                                _keySubscribers[(keyValue, typeof(T))] : new List<Delegate>();
                if (!delegates.Contains(subscription))
                {
                    delegates.Add(subscription);
                }
                _keySubscribers[(keyValue, typeof(T))] = delegates;
            }

            public void Unsubscribe<T>(string keyValue, Action<MessagePayload<T>> subscription)
            {
                if (!_keySubscribers.ContainsKey((keyValue, typeof(T)))) return;
                var delegates = _keySubscribers[(keyValue, typeof(T))];
                if (delegates.Contains(subscription))
                    delegates.Remove(subscription);
                if (delegates.Count == 0)
                    _keySubscribers.Remove((keyValue,typeof(T)));
            }

            public void Dispose()
            {
                _subscribers?.Clear();
            }
        }
    }
}
