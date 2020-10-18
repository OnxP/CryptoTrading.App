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
    public class Broker: IBroker
    {
        private readonly IMarket _market;
        private readonly ILogger _logger;
        private readonly IPositions _currentPositions;

        public Broker(IMarket market, ILogger logger, IPositions positions, IMarketDataEvents marketDataEvents)
        {
            _market = market;
            _logger = logger;
            _currentPositions = positions;
            ConfigureMessageBroker();
        }


        private void ConfigureMessageBroker()
        {
            IMessageBroker messageBroker = MessageBroker.Instance;

            Action<MessagePayload<ITradeRequest>> processMessage = ProcessMessageAction;

            messageBroker.Subscribe(processMessage);
        }

        private void ProcessMessageAction(MessagePayload<ITradeRequest> obj)
        {
            ITradeRequest request = obj.What;
            //check if there is an open position and check if we have enough BTC balance
            if (_currentPositions.CheckOpenPosition(request.BuySymbol) &&_currentPositions.CheckBalance(request.SellSymbol, request.SellAmount))
            {
                //check to see if there is sufficient funds
                var trade = _currentPositions.CreateTrade(request, new StopLossMonitor(this));
                //set market order
                var order = _market.SetMarketOrder(trade).Result;
                //confirm market order has been met
                LogOrder(order,OrderStatus.Filled);
                _currentPositions.UpdatePosition(order);
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
