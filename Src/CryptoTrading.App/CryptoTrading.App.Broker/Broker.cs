using System;
using System.ComponentModel;
using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Message_Broker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CryptoTrading.App.Broker
{
    public class Broker: IBroker
    {
        private readonly IMarket _market;
        private readonly ILogger _logger;
        private IPositions _currentPositions;

        public Broker( IMarket market, ILogger logger, IPositions positions )
        {
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
                var trade = _currentPositions.CreatePosition(request, new StopLossMonitor(this));
                //set market order
                SetMarketOrder(trade);
                //confirm market order has been met
                
            }
        }

        private void SetMarketOrder(ITrade trade)
        {
            throw new NotImplementedException();
        }

        public void SetLimitOrder(ITrade trade, decimal currentStopLoss)
        {
            throw new System.NotImplementedException();
        }

        public void SetNewLimitOrder(ITrade trade, decimal currentStopLoss)
        {
            CancelLimitOrder(trade);
            SetLimitOrder(trade,currentStopLoss);
        }

        private void CancelLimitOrder(ITrade trade)
        {
            throw new System.NotImplementedException();
        }

        public void ClosePosition(ITrade trade)
        {
            throw new System.NotImplementedException();
        }
    }
}
