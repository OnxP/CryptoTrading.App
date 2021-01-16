using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CryptoTrading.App.Broker
{
    public class CryptoBroker: IBroker
    {
        private readonly IMarket _market;
        private readonly ILogger<CryptoBroker> _logger;

        public CryptoBroker(IMarket market, ILogger<CryptoBroker> logger)
        {
            _market = market;
            _logger = logger;
            ConfigureMessageBroker();
        }


        private void ConfigureMessageBroker()
        {
            IMessageBroker messageBroker = MessageBroker.Instance;

            Action<MessagePayload<IMarketRequest>> NewTradeMesssage = ProcessMessageAction;
            messageBroker.Subscribe(NewTradeMesssage);

            Action<MessagePayload<ICancelRequest>> CancelTradeMessage = ProcessMessageAction;
            messageBroker.Subscribe(CancelTradeMessage);

            Action<MessagePayload<IStopLimitRequest>> StoplimitTradeMessage = ProcessMessageAction;
            messageBroker.Subscribe(StoplimitTradeMessage);

        }

        private void ProcessMessageAction(MessagePayload<IMarketRequest> obj)
        {
            IMarketRequest request = obj.What;
            
            //set market order
            var order = _market.SetMarketOrder(request).Result;
            //confirm market order has been met
            LogOrder(order,OrderStatus.Filled);
            MessageBroker.Instance.Publish(obj.Who, order);
        }

        private void ProcessMessageAction(MessagePayload<ICancelRequest> obj)
        {
            ICancelRequest request = obj.What;
            //set market order
            var order = _market.CancelOrder(request).Result;
            //confirm market order has been met
            MessageBroker.Instance.Publish(obj.Who, order);
        }
        private void ProcessMessageAction(MessagePayload<IStopLimitRequest> obj)
        {
            IStopLimitRequest request = obj.What;
            //set market order
            var order = _market.SetLimitOrder(request).Result;
            //confirm market order has been met
            LogOrder(order, OrderStatus.New);
            MessageBroker.Instance.Publish(obj.Who, order);
        }

        private void LogOrder(Order order, OrderStatus status)
        {
            //todo log order to the database.
        }

        public void ClosePosition(ITrade trade)
        {
            //IEnumerable<Order> orders = await _market.GetAllOpenOrders();
            //foreach (var order in orders)
            //{
            //    await _market.CancelOrder(order);
            //}
        }
    }
}
