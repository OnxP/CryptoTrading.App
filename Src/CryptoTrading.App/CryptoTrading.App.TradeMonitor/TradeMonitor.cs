using Binance;
using CryptoTrading.App.Broker;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using CryptoTrading.App.Monitor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CryptoTrading.App.TradeMonitor
{
    public class TradeMonitor 
    {
        public IPositions Positions { get; set; }

        public List<ITrade> Trades { get; set; }
        public List<ITransactionMonitor> OrderMonitors { get; set; }

        public IEnumerable<ITrade> LiveTrades => Trades.Where(x => x.Open);

        public IEnumerable<ITransactionMonitor> CurrentMonitors => OrderMonitors.Where(x => x.Live);

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
            if (obj.Who is Transaction transaction)
            {
                Order order = obj.What;
                //assume that order has been filled.
                var trade = OrderMonitors.First(x => x.Symbol == order.Symbol);
                trade.Update(order);
                //trade monitor takes the order and decides what to do.


                //find current transaction and update.
                //if closed then do nothing
                //if open then pass to stoploss monitor.
                //if not filled then decide what todo???
            }
        }

        private void ProcessMessageAction(MessagePayload<string> obj)
        {
            if (obj.Who is Transaction transaction)
            {
                string order = obj.What;
                //assume that order has been filled.
                var trade = OrderMonitors.First(x => x.Symbol == transaction.Pair);
                trade.Cancel(order);
                //set market order
                //find current transaction and cancel it.
            }
        }

        private void ProcessMessageAction(MessagePayload<ITradeRequest> obj)
        {
            if(Positions.CheckRequest(obj.What))
            {
                var trade = Positions.CreateTrade(obj.What);
                Trades.Add(trade);
                IMarketRequest request = new MarketRequest(trade.CurrentTransaction);

                MessageBroker.Instance.Publish(trade.CurrentTransaction, request);
            }
        }
    }
}

