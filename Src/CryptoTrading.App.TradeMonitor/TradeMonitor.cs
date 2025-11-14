using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using System;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Database.StoreTrades;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using CryptoTrading.App.Core.Position;

namespace CryptoTrading.App.Monitor
{
    //Monitors a Trade, and manages transactions.
    //Each transaction is linked to an order.
    //The trade is considered to be live if there are open transactions
    class TradeMonitor : ITradeMonitor
    {
        private ILogger<TradeMonitor> Logger { get; set; }
        private IConfig Config { get; set; }
        public TradeMonitor(ILogger<TradeMonitor>logger,IMarketMonitor monitor,IConfig config)
        {
            marketMonitor = monitor;
            Logger = logger;
            Config = config;
        }

        public ITrade Trade { get; set; }
        public decimal CurrentStopLimit { get; set; }
        public IMarketMonitor marketMonitor { get; set; }
        public IStopLimitTracker Tracker => Trade.StopLimitTracker;

        public DateTime currentCloseTime { get; set; }
        public ITradeRequest Request { get; set; }

        public async Task SubscribetToMarketData()
        {
            if (!marketMonitor.IsSubscribed(Request.Symbol, KeyValue))
            {
                var candleSticks = await marketMonitor.GetHistoricCandleSticks();
                Request.Strategy.LoadHistoricCandleSticks(candleSticks);
                marketMonitor.Subscribe(Request.Symbol, KeyValue, ProcessCandleStick);
            }
        }

        public async void ProcessCandleStick(CandlestickEventArgs candleStick)
        {
            var closePrice = candleStick.Candlestick.Close;
            Trade.CurrentPrice = closePrice;
            currentCloseTime = candleStick.Candlestick.CloseTime;
            Tracker.CurrentPrice = closePrice;
            //Logger.LogInformation($"Process CandleStick {Symbol} at {currentCloseTime} PRICE: {closePrice} - Target {Tracker.TargetPrice} : Stop Limit: {Tracker.StopLimitPrice}");
            if (Tracker.RequestUpdateOfStopLimit(candleStick.Candlestick.High))
            {
                UpdateStopLimit(Tracker.TargetPrice< candleStick.Candlestick.High);
            }
            if (candleStick.Candlestick.Low <= Tracker.StopLimitPrice)
            {
                DbCandleStickManagement.PauseFlow = true;
                //check for fill order
                if (await marketMonitor.CheckOrder(Trade.CurrentTransaction))
                {
                    Trade.CurrentTransaction.TransactionDate = currentCloseTime;
                    Tracker.EndDateTime = currentCloseTime;
                    Trade.Open = false;
                    
                    //unsubscribe to monitor
                    marketMonitor.UnSubscribe(candleStick.Candlestick.Symbol, KeyValue);
                    Tracker.IsOpen = false;
                    Logger.LogInformation($"Completed Trade {Trade.Pair} finalPrice {Trade.CurrentTransaction.Price} profit {(Trade.Profit/100):P}");
                    if(Config != null)
                    {
                        var factory = new ArchiveTradeFactory(Config);
                        var trade = factory.CreateHistoricTrades(Trade);
                        StoreTradesToDb(trade, Config);
                    }
                    Dispose();
                    DbCandleStickManagement.PauseFlow = false;
                    return;
                }
                else
                {
                    //partial fill of the order. should just continue...
                }
                
            }

            
            DbCandleStickManagement.PauseFlow = false;
        }

        public static void StoreTradesToDb(HistoricTrades completedTrade, IConfig config)
        {
            using var context = new HistoricTradeContext(config.StoreTradesConnectionString);
            context.HistoricTrades.Add(completedTrade);
            context.SaveChanges();
        }

        private void Dispose()
        {
            Tracker.Close();
            
            marketMonitor = null;
        }

        private async Task CreateNewStopLimitOrder()
        {
            Trade.CreateStopLimitTransaction(Tracker.StopLimitPrice,currentCloseTime);
            
            IStopLimitRequest request = new StopLimitRequest(Trade.CurrentTransaction);
            //request.StopPrice = request.Price;
            await MessageBroker.Instance.Publish(KeyValue,Trade.CurrentTransaction, request);
        }

        private void CancelLimitOrder(int count = 0)
        {
            if (count >= 2) return;
            try
            {
                long orderId = 0;
                if (Trade.CurrentTransaction.Order != null && Trade.CurrentTransaction.Order.Id != null )
                {
                    orderId = Trade.CurrentTransaction.Order.Id;
                    ICancelRequest request = new CancelRequest(orderId, Trade.Pair);
                    MessageBroker.Instance.Publish(KeyValue, Trade.CurrentTransaction, request);

                }
                else
                {
                    count++;
                    CancelLimitOrder(count);
                }
                
            }
            catch
            {
                count++;
                CancelLimitOrder(count);
            }
        }

        public bool Live => Trade.Open;
        public override string ToString()
        {
            return $"{Symbol} - {currentCloseTime:s}";
        }

        public string Symbol => Trade.Pair;

        public string KeyValue { get; set; } = "1";

        public void CancelLimitOrder(string order)
        {
            Trade.CancelCurrentTransaction();//cancel order not updated properly.
            CreateNewStopLimitOrder();
        }

        public void UpdateInitialTransaction(Order order)
        {
            Trade.UpdateCurrentTransaction(order);
            if (order.Status == OrderStatus.Filled)
            {
                Tracker.Configure(order);
            }
            CreateNewStopLimitOrder();
            
        }

        private void UpdateStopLimit(bool targetReached)
        {
            if(targetReached) Tracker.MoveStopLimit();
            CancelLimitOrder();
            //CreateNewStopLimitOrder();
        }

        public void UpdateStopLimitOrder(Order order)
        {
            Trade.UpdateCurrentTransaction(order);//order not updated properly.
        }

        public void AddRequest(ITrade trade)
        {
            Trade = trade;
        }

        Task ITradeMonitor.CancelLimitOrder(string order)
        {
            throw new NotImplementedException();
        }

        public void AddRequest(ITradeRequest request, IPositions positions)
        {
            Request = request;
            IMessageBroker messageBroker = MessageBroker.Instance;

            Func<MessagePayload<Order>, Task> newTradeMesssage = ProcessMessageAction;
            messageBroker.Subscribe(Request.Symbol, newTradeMesssage);

            Func<MessagePayload<string>, Task> CancelTradeMessage = ProcessMessageAction;
            messageBroker.Subscribe(Request.Symbol, CancelTradeMessage);
        }

        private Task ProcessMessageAction(MessagePayload<Order> obj)
        {
            if (obj.Who is ITransaction transaction)
            {
                Order order = obj.What;
                //assume that order has been filled.
                try
                {
                    //error occurs here because there are not monitors set up for trade...need to find out why!
   
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
            return Task.CompletedTask;
        }

        private async Task ProcessMessageAction(MessagePayload<string> obj)
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
                        await trade.CancelLimitOrder(order);
                        break;
                    case TransactionType.Transaction:
                    case TransactionType.MarketTransaction:
                        break;
                }
                //set market order
                //find current transaction and cancel it.
            }
        }

        public void CompleteTrade()
        {
            throw new NotImplementedException();
        }

        
    }
}
