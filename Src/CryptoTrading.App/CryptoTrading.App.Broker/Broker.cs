using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Message_Broker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CryptoTrading.App.Broker
{
    public class Broker: IBroker
    {
        private readonly IMarket _market;
        private readonly IBinanceApiUser _user;
        private readonly ILogger _logger;
        private IPositions _currentPositions;

        public Broker(IBinanceApiUser user, IMarket market, ILogger logger, IPositions positions )
        {
            _user = user;
            _market = market;
            _logger = logger;
            _currentPositions = positions;
            Configure();
            ConfigureMessageBroker();
        }

        private void Configure()
        {
            //connect to the exchange and download current account balances.
            BulidCurrentPositions(_market.GetAccountBalances());
            //load any currently pending transactions and decide if to delete them or wait for them to hit..i.e. stoplosses and limit orders
            _market.GetPendingTransactions();
            //configure the message bus to read incomming messages/request for trades.
            //need to check the current timestamp on the server and the timestamp on the messages. May not need to to this as the messages don't queue
        }

        private void BulidCurrentPositions(object v)
        {
            throw new NotImplementedException();
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
                var order = _market.SetMarketOrder(trade, _user).Result;
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
            var order = await _market.SetLimitOrder(trade, _user, currentStopLoss);

            return order;
        }

        public async Task<Order> SetNewLimitOrder(ITrade trade, Order order, decimal currentStopLoss)
        {
            var result = await _market.CancelOrder(order, _user);
            var newOrder = await _market.SetLimitOrder(trade, _user, currentStopLoss);

            return newOrder;
        }

        public async void ClosePosition(ITrade trade)
        {
            IEnumerable<Order> orders = await _market.GetAllOpenOrders(_user);
            foreach (var order in orders)
            {
                await _market.CancelOrder(order,_user);
            }
        }
    }
}
