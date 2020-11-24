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
    public class TradeProcessor 
    {
        public IPositions Positions { get; set; }

        public List<ITrade> Trades { get; set; }
        public List<ITradeMonitor> OrderMonitors { get; set; }

        public IEnumerable<ITrade> LiveTrades => Trades.Where(x => x.Open);

        public IEnumerable<ITradeMonitor> CurrentMonitors => OrderMonitors.Where(x => x.Live);

        public TradeProcessor(IPositions positions)
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
            if (obj.Who is ITransaction transaction)
            {
                Order order = obj.What;
                //assume that order has been filled.
                var trade = OrderMonitors.First(x => x.Symbol == order.Symbol);
                switch (transaction.Type)
                {

                    case TransactionType.StopLimitTransaction:
                        trade.StartStopLossMonitor(order);
                        break;
                    case TransactionType.Transaction:
                    case TransactionType.MarketTransaction:
                        trade.UpdateInitialTransaction(order);
                        break;
                }
            }
        }

        private void ProcessMessageAction(MessagePayload<string> obj)
        {
            if (obj.Who is ITransaction transaction)
            {
                string order = obj.What;
                //assume that order has been filled.
                var trade = OrderMonitors.First(x => x.Symbol == transaction.Pair);
                switch (transaction.Type)
                {
                    case TransactionType.StopLimitTransaction:
                        trade.CancelLimitOrder(order);
                        break;
                    case TransactionType.Transaction:
                    case TransactionType.MarketTransaction:
                        break;
                }
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
                var tradeMonitor = new TradeMonitor(trade);
                OrderMonitors.Add(tradeMonitor);
            }
        }
    }
}

