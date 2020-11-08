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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CryptoTrading.App.Broker
{
    public class CryptoBroker: IBroker
    {
        private readonly IMarket _market;
        private readonly ILogger _logger;
        private readonly IPositions _currentPositions;

        public CryptoBroker(IMarket market, ILogger logger, IPositions positions)
        {
            _market = market;
            _logger = logger;
            _currentPositions = positions;
            ConfigureMessageBroker();
        }


        private void ConfigureMessageBroker()
        {
            IMessageBroker messageBroker = MessageBroker.Instance;

            Action<MessagePayload<IMarketRequest>> processMessage = ProcessMessageAction;
            Action<MessagePayload<ICancelRequest>> processMessage = ProcessMessageAction;
            Action<MessagePayload<IStopLimitRequest>> processMessage = ProcessMessageAction;

            messageBroker.Subscribe(processMessage);
        }

        private void ProcessMessageAction(MessagePayload<IMarketRequest> obj)
        {
            ITradeRequest request = obj.What;

            //check if there is an open position and check if we have enough BTC balance
            if (!_currentPositions.CheckOpenPosition(request.BaseSymbol) && _currentPositions.CheckBalance(request.QuoteSymbol, request.SellPercentage))
            {
                //check to see if there is sufficient funds
                var trade = _currentPositions.CreateTrade(request);
                //set market order
                var order = _market.SetMarketOrder(trade).Result;
                trade.CurrentTransaction.AddOrder(order);
                //confirm market order has been met
                LogOrder(order,OrderStatus.Filled);

                MessageBroker.Instance.Publish("Broker", trade);


                //var stopLoss = _currentPositions.CalculateStoploss(order);

                //var stopLimitOrder = SetLimitOrder(trade, stopLoss).Result;
                //LogOrder(stopLimitOrder, OrderStatus.Filled);
                //_currentPositions.AddOrder(stopLimitOrder);
            }
        }

        private void ProcessMessageAction(MessagePayload<ICancelRequest> obj)
        {
            ITradeRequest request = obj.What;

            //check if there is an open position and check if we have enough BTC balance
            if (!_currentPositions.CheckOpenPosition(request.BaseSymbol) && _currentPositions.CheckBalance(request.QuoteSymbol, request.SellPercentage))
            {
                //check to see if there is sufficient funds
                var trade = _currentPositions.CreateTrade(request);
                //set market order
                var order = _market.SetMarketOrder(trade).Result;
                //confirm market order has been met
                LogOrder(order, OrderStatus.Filled);

                MessageBroker.Instance.Publish("Broker", trade);
            }
        }
        private void ProcessMessageAction(MessagePayload<IStopLimitRequest> obj)
        {
            ITradeRequest request = obj.What;

            //check if there is an open position and check if we have enough BTC balance
            if (!_currentPositions.CheckOpenPosition(request.BaseSymbol) && _currentPositions.CheckBalance(request.QuoteSymbol, request.SellPercentage))
            {
                //check to see if there is sufficient funds
                var trade = _currentPositions.CreateTrade(request);
                //set market order
                var order = _market.SetMarketOrder(trade).Result;
                //confirm market order has been met
                LogOrder(order, OrderStatus.Filled);

                MessageBroker.Instance.Publish("Broker", trade);
            }
        }

        private void LogOrder(Order order, OrderStatus status)
        {
            //todo log order to the database.
        }


        public async Task<Order> SetLimitOrder(ITrade trade, decimal currentStopLoss)
        {
            var order = await _market.SetLimitOrder(trade, currentStopLoss);

            return order;
        }

        public async Task<Order> SetNewLimitOrder(ITrade trade, Order order, decimal currentStopLoss)
        {
            var result = await _market.CancelOrder(order);
            var newOrder = await _market.SetLimitOrder(trade, currentStopLoss);

            return newOrder;
        }

        public async void ClosePosition(ITrade trade)
        {
            IEnumerable<Order> orders = await _market.GetAllOpenOrders();
            foreach (var order in orders)
            {
                await _market.CancelOrder(order);
            }
        }
    }
}
