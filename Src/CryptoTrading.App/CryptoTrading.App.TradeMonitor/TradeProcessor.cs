using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CryptoTrading.App.Monitor
{
    public class TradeProcessor : ITradeProcessor
    {
        public IPositions Positions { get; set; }
        private IMarketMonitorFactory TradeFactory {get;set;}

        private static readonly object _lock = new object();
        public List<ITrade> Trades { get; set; }
        public List<ITradeMonitor> OrderMonitors { get; set; }

        public IEnumerable<ITrade> LiveTrades => Trades.Where(x => x.Open);

        public IEnumerable<ITradeMonitor> CurrentMonitors => OrderMonitors.Where(x => x.Live);

        public TradeProcessor(IPositions positions, IMarketMonitorFactory factory)
        {
            Positions = positions;
            TradeFactory = factory;
            Trades = new List<ITrade>();
            OrderMonitors = new List<ITradeMonitor>();
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
                        trade.UpdateStopLimitOrder(order);
                        break;
                    case TransactionType.Transaction:
                        break;
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
            lock (_lock)
            {
                if(CheckCurrentOrderMonitors(obj.What.BaseSymbol + obj.What.QuoteSymbol))
                {
                    //do you want to scale in again
                }
                else if (Positions.CheckRequest(obj.What))
                {
                    var trade = Positions.CreateTrade(obj.What);
                    Trades.Add(trade);
                    var tradeMonitor = TradeFactory.CreateMonitor(trade);
                    OrderMonitors.Add(tradeMonitor);
                    //create Market Order
                    var marketOrder = new MarketRequest(trade.CurrentTransaction);
                    MessageBroker.Instance.Publish<IMarketRequest>(trade.CurrentTransaction, marketOrder);
                }
            }
        }

        private bool CheckCurrentOrderMonitors(string symbol)
        {
            if (OrderMonitors.Count() != 0) return false;
            if (OrderMonitors.LastOrDefault(x => x.Symbol == symbol) != null)
            {
                return OrderMonitors.LastOrDefault(x => x.Symbol == symbol).Live;
            }

            return false;
        }

        public void CompleteAllTransactions()
        {
            Trades.Where(x=>x.CurrentTransaction.Status==TransactionStatus.Pending).ToList().ForEach(x=>x.CompleteTrade());
            //convert all positions to BTC.
        }
    }
}

