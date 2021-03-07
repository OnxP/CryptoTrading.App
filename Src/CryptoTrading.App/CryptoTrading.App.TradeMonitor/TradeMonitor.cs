using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Monitor
{
    //Monitors a Trade, and manages transations.
    //Each transaction is linked to an order.
    //The trade is considered to be live if there are open transactions
    class TradeMonitor : ITradeMonitor
    {
        public TradeMonitor(IMarketMonitor monitor, IStopLimitTracker tracker)
        {
            Tracker = tracker;
            marketMonitor = monitor;
        }

        public ITrade Trade { get; set; }
        public decimal CurrentStopLimit { get; set; }
        public IMarketMonitor marketMonitor { get; set; }
        public IStopLimitTracker Tracker { get; set; }

        public DateTime currentCloseTime { get; set; }

        public void ProcessCandleStick(CandlestickEventArgs candleStick)
        {
            var closePrice = candleStick.Candlestick.Close;
            Trade.CurrentPrice = closePrice;
            currentCloseTime = candleStick.Candlestick.CloseTime;
            Tracker.CurrentPrice = closePrice;


            if (candleStick.Candlestick.Low <= Tracker.StopLimitPrice)
            {
                //check for fill order
                if (marketMonitor.CheckOrder(Trade.CurrentTransaction))
                {
                    Trade.CurrentTransaction.TransactionDate = currentCloseTime;
                    Trade.Open = false;
                    //stop monitor??
                    marketMonitor.StopStream();
                    Dispose();
                }
                else
                {
                    //partial fill of the order. should just continue...
                }
                
            }

            if (closePrice >= Tracker.TargetPrice)
            {
                UpdateStopLimit();
            }
        }

        private void Dispose()
        {
            marketMonitor.Dispose();
            Tracker.Dispose();
            marketMonitor = null;
        }

        private void CreateNewStopLimitOrder()
        {
            Trade.CreateStopLimitTransaction(Tracker.StopLimitPrice,currentCloseTime);
            
            IStopLimitRequest request = new StopLimitRequest(Trade.CurrentTransaction);
            request.StopPrice = Tracker.StopLimitPrice;
            MessageBroker.Instance.Publish(Trade.CurrentTransaction, request);
        }

        private void CancelLimitOrder()
        {
            //Trade.CreateStopLimitTransaction(Tracker.StopLimitPrice);

            ICancelRequest request = new CancelRequest(Trade.CurrentTransaction.Order.Id, Trade.Symbol);
            MessageBroker.Instance.Publish(Trade.CurrentTransaction, request);
        }

        public bool Live => Trade.Open;

        public string Symbol => Trade.Symbol;

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
            if (!marketMonitor.Started) marketMonitor.StartStream();
        }

        private void UpdateStopLimit()
        {
            Tracker.MoveStopLimit();
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
            marketMonitor.Symbol = trade.Symbol;
            marketMonitor.Subscribe(ProcessCandleStick);
        }
    }
}
