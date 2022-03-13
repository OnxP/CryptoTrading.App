using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Monitor
{
    public class TradeProcessor : ITradeProcessor
    {
        public IPositions Positions { get; set; }
        private IMarketMonitorFactory TradeFactory {get;}

        private readonly object _lock = new object();
        public List<ITradeMonitor> OrderMonitors { get; set; }
        public IEnumerable<ITrade> LiveTrades => OrderMonitors.Where(x => x.Live).Select(x=>x.Trade);
        public string KeyValue { get; set; }
        public IEnumerable<ITradeMonitor> CurrentMonitors
        {
            get
            {
                lock (_lock)
                {
                    return OrderMonitors.Where(x => x.Live).ToList();
                }
            }
        }

        private ILogger<TradeProcessor> Logger { get; set; }
        public TradeProcessor(ILogger<TradeProcessor> logger ,IMarketMonitorFactory factory)
        {
            TradeFactory = factory;
            OrderMonitors = new List<ITradeMonitor>();
            KeyValue = string.IsNullOrEmpty(KeyValue) ? "1" : KeyValue;
            Logger = logger;
            ConfigureMessageBroker();
        }
        public TradeProcessor(ILogger<TradeProcessor> logger,IPositions positions, IMarketMonitorFactory factory):this(logger,factory)
        {
            Positions = positions;
        }

        public TradeProcessor(IPositions positions, ILogger<TradeProcessor> logger,IMarketMonitorFactory factory, IKey key): this(logger,positions, factory)
        {
            KeyValue = key.KeyValue;
        }
        private void ConfigureMessageBroker()
        {
            IMessageBroker messageBroker = MessageBroker.Instance;

            Action<MessagePayload<Order>> newTradeMesssage = ProcessMessageAction;
            messageBroker.Subscribe(KeyValue,newTradeMesssage);

            Action<MessagePayload<string>> CancelTradeMessage = ProcessMessageAction;
            messageBroker.Subscribe(KeyValue,CancelTradeMessage);

            Action<MessagePayload<ITradeRequest>> TradeRequestMessage = ProcessMessageAction;
            messageBroker.Subscribe(KeyValue, TradeRequestMessage);
        }

        private void ProcessMessageAction(MessagePayload<Order> obj)
        {
            if (obj.Who is ITransaction transaction)
            {
                Order order = obj.What;
                //assume that order has been filled.
                try
                {
                    //error occurs here because there are not monitors set up for trade...need to find out why!
                    var trade = OrderMonitors.Last(x => x.Symbol == order.Symbol);
                
                    switch (transaction.Type)
                    {
                        case TransactionType.StopLimitTransaction:
                            trade.UpdateStopLimitOrder(order);
                            break;
                        case TransactionType.Transaction:
                            break;
                        case TransactionType.MarketTransaction:
                            trade.UpdateInitialTransaction(order);
                            Logger.LogInformation($"Completed Trade for {order.Symbol} Q: {order.ExecutedQuantity} Price: {order.Price} originalQ: {order.OriginalQuantity} Original Price: {trade.Trade.CurrentPrice}");
                            break;
                    }
                }
                catch
                {
                    //if the stoploss is hit while the next order is being placed then we need to cancel and pull out of the trade
                    //for now just skip and continue.

                }
            }
        }

        private void ProcessMessageAction(MessagePayload<string> obj)
        {
            if (obj.Who is ITransaction transaction)
            {
                string order = obj.What;
                if (transaction.Status == TransactionStatus.Completed) return;
                //assume that order has been filled.
                var trade = CurrentMonitors.First(x => x.Symbol == transaction.Pair);
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
                else if (Positions.CheckRequest(obj.What) && LiveTrades.Count()<Config.NoOfTrades)
                {
                    DbCandleStickManagement.PauseFlow = true;
                    //need to pause the data stream.
                    var trade = Positions.CreateTrade(obj.What);
                    var tradeMonitor = TradeFactory.CreateMonitor(trade);
                    tradeMonitor.KeyValue = KeyValue;
                    OrderMonitors.Add(tradeMonitor);
                    //create Market Order
                    var marketOrder = new MarketRequest(trade.CurrentTransaction);
                    MessageBroker.Instance.Publish<IMarketRequest>(KeyValue,trade.CurrentTransaction, marketOrder);
                    Logger.LogInformation($"Place Trade for {trade.Symbol} Q: {trade.Quantity} BTC amt: {trade.CurrentTransaction.Quote.Quantity}");
                }
            }
        }

        private bool CheckCurrentOrderMonitors(string symbol)
        {
            if (CurrentMonitors.Count() != 0) return false;
            return CurrentMonitors.LastOrDefault(x => x.Symbol == symbol) != null && CurrentMonitors.LastOrDefault(x => x.Symbol == symbol)!.Live;
        }

        public void CompleteAllTransactions()
        {
            LiveTrades.ToList().ForEach(x=>x.CompleteTrade());
            //Trades.Where(x=>x.CurrentTransaction.Status==TransactionStatus.Pending).ToList().ForEach(x=>x.CompleteTrade());
            //convert all positions to BTC.
        }

        public void ClearInactiveTrades()
        {
            lock (_lock)
            {
                OrderMonitors.RemoveAll(x => !x.Live);
            }
        }

        public List<ITrade> GetCompletedTrades()
        {
            return OrderMonitors.Where(x => !x.Live).Select(x=>x.Trade).ToList();
        }
        public IConfig Config { get; set; }
        public void Configure(IConfig config)
        {
            Config = config;
        }
    }
}

