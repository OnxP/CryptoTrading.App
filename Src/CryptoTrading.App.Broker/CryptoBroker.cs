using System;
using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Broker
{
    public class CryptoBroker: IBroker
    {
        private readonly IMarket _market;
        private readonly ILogger<CryptoBroker> _logger;
        private string KeyValue { get; set; }
        public CryptoBroker(IMarket market, ILogger<CryptoBroker> logger)
        {
            _market = market;
            _logger = logger;
            KeyValue = string.IsNullOrEmpty(KeyValue) ? "1" : KeyValue;
            ConfigureMessageBroker();
        }

        public CryptoBroker(IMarket market, ILogger<CryptoBroker> logger, IKey key) : this(market,logger)
        {
            KeyValue = key.KeyValue;

        }


        private void ConfigureMessageBroker()
        {
            IMessageBroker messageBroker = MessageBroker.Instance;

            Action<MessagePayload<IMarketRequest>> NewTradeMesssage = ProcessMessageAction;
            messageBroker.Subscribe(KeyValue, NewTradeMesssage);

            Action<MessagePayload<ICancelRequest>> CancelTransactionMessage = ProcessMessageAction;
            messageBroker.Subscribe(KeyValue, CancelTransactionMessage);

            Action<MessagePayload<IStopLimitRequest>> StoplimitTradeMessage = ProcessMessageAction;
            messageBroker.Subscribe(KeyValue, StoplimitTradeMessage);

        }

        private async void ProcessMessageAction(MessagePayload<IMarketRequest> obj)
        {
            IMarketRequest request = obj.What;
                //set market order
                var order = await _market.SetMarketOrder(request).ConfigureAwait(false);
                //confirm market order has been met
                LogOrder(order, OrderStatus.Filled);
                MessageBroker.Instance.Publish(KeyValue, obj.Who, order);
            }

        private void ProcessMessageAction(MessagePayload<ICancelRequest> obj)
        {
            ICancelRequest request = obj.What;
            //set market order
/* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            var order = await _market.CancelOrder(request).ConfigureAwait(false);
            //confirm market order has been met
            MessageBroker.Instance.Publish(KeyValue,obj.Who, order);
        }
        private async void ProcessMessageAction(MessagePayload<IStopLimitRequest> obj)
        {
            IStopLimitRequest request = obj.What;
            //set market order
            var order = await _market.SetLimitOrder(request).ConfigureAwait(false);
            //confirm market order has been met
            LogOrder(order, OrderStatus.New);
            MessageBroker.Instance.Publish(KeyValue,obj.Who, order);
        }

        private void LogOrder(Order order, OrderStatus status)
        {
            //todo log order to the database.
        }

        public void ClosePosition(ITrade trade)
        {
            //IEnumerable<Order> orders = await _market.GetAllOpenOrders().ConfigureAwait(false);
            //foreach (var order in orders)
            //{
            //    await _market.CancelOrder(order).ConfigureAwait(false);
            //}
        }
    }
}