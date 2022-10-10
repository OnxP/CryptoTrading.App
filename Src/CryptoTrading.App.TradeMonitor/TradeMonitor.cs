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

        public void ProcessCandleStick(CandlestickEventArgs candleStick)
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
                if (marketMonitor.CheckOrder(Trade.CurrentTransaction))
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

        private void CreateNewStopLimitOrder()
        {
            Trade.CreateStopLimitTransaction(Tracker.StopLimitPrice,currentCloseTime);
            
            IStopLimitRequest request = new StopLimitRequest(Trade.CurrentTransaction);
            //request.StopPrice = request.Price;
            MessageBroker.Instance.Publish(KeyValue,Trade.CurrentTransaction, request);
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
            if (!marketMonitor.IsSubscribed(order.Symbol, KeyValue)) marketMonitor.Subscribe(order.Symbol,KeyValue, ProcessCandleStick);
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

        public void AddTrade(ITrade trade)
        {
            Trade = trade;
        }
    }
}
