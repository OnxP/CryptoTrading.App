using Binance;
using CryptoTrading.App.Broker;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using System;
using System.Runtime.CompilerServices;

namespace CryptoTrading.App.TradeMonitor
{
    public class TradeMonitor : ITradeMonitor
    {
        public IPositions Positions { get; set; }

        public TradeMonitor(IPositions positions)
        {
            Positions = positions;
            ConfigureMessageBroker();
        }
        private void ConfigureMessageBroker()
        {
            IMessageBroker messageBroker = MessageBroker.Instance;

            Action<MessagePayload<Order>> NewTradeMesssage = ProcessMessageAction;
            messageBroker.Subscribe(NewTradeMesssage);

            Action<MessagePayload<string>> CancelTradeMessage = ProcessMessageAction;
            messageBroker.Subscribe(CancelTradeMessage);

            Action<MessagePayload<ITradeRequest>> TradeRequestMessage = ProcessMessageAction;
            messageBroker.Subscribe(TradeRequestMessage);
        }

        private void ProcessMessageAction(MessagePayload<Order> obj)
        {
            if (obj.Who is IRequest request) return;
            Order order = obj.What;

        }

        private void ProcessMessageAction(MessagePayload<string> obj)
        {
            if (obj.Who is ICancelRequest request)
            {
                string order = obj.What;
                //set market order
            }
        }

        private void ProcessMessageAction(MessagePayload<ITradeRequest> obj)
        {
            
        }
    }
}

