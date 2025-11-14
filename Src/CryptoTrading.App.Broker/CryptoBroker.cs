using System;
using System.Threading.Tasks;
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

            Func<MessagePayload<IMarketRequest>,Task> MarketTradeMesssage = ProcessMessageAction;
            messageBroker.Subscribe(KeyValue, MarketTradeMesssage);

            Func<MessagePayload<ILimitRequest>, Task> LimitTradeMesssage = ProcessMessageAction;
            messageBroker.Subscribe(KeyValue, LimitTradeMesssage);

            Func<MessagePayload<ICancelRequest>,Task> CancelTransactionMessage = ProcessMessageAction;
            messageBroker.Subscribe(KeyValue, CancelTransactionMessage);

            Func<MessagePayload<IStopLimitRequest>,Task> StoplimitTradeMessage = ProcessMessageAction;
            messageBroker.Subscribe(KeyValue, StoplimitTradeMessage);

        }

        private async Task ProcessMessageAction(MessagePayload<IMarketRequest> obj)
        {
            IMarketRequest request = obj.What;
            //set market order
            var order = await _market.SetMarketOrder(request).ConfigureAwait(false);
            //confirm market order has been met
            LogOrder(order, OrderStatus.Filled);
            await MessageBroker.Instance.Publish(order.Symbol, obj.Who, order);
        }

        private async Task ProcessMessageAction(MessagePayload<ILimitRequest> obj)
        {
            ILimitRequest request = obj.What;
            //set market order
            var order = await _market.SetLimitOrder(request).ConfigureAwait(false);
            //confirm market order has been met
            LogOrder(order, OrderStatus.Filled);
            await MessageBroker.Instance.Publish(order.Symbol, obj.Who, order);
        }

        private async Task ProcessMessageAction(MessagePayload<ICancelRequest> obj)
        {
            ICancelRequest request = obj.What;
            //set market order
            var order = await _market.CancelOrder(request).ConfigureAwait(false);
            //confirm market order has been met
            await MessageBroker.Instance.Publish(request.Symbol, obj.Who, order);
        }
        private async Task ProcessMessageAction(MessagePayload<IStopLimitRequest> obj)
        {
            IStopLimitRequest request = obj.What;
            //set market order
            var order = await _market.SetStopLimitOrder(request).ConfigureAwait(false);
            //confirm market order has been met
            LogOrder(order, OrderStatus.New);
            await MessageBroker.Instance.Publish(order.Symbol, obj.Who, order);
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