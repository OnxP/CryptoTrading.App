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
        public TradeMonitor(IMarketMonitor monitor)
        {
            marketMonitor = monitor;
        }

        public ITrade Trade { get; set; }
        public decimal CurrentStopLimit { get; set; }
        public IMarketMonitor marketMonitor { get; set; }
        public IStopLimitTracker Tracker { get; set; }

        public void ProcessCandleStick(CandlestickEventArgs candleStick)
        {
            var closePrice = candleStick.Candlestick.Close;

            if (closePrice >= Tracker.TargetPrice)
            {
                UpdateStopLimit();
            }
            if (closePrice <= Tracker.StopLimitPrice)
            {
                //check for fill order
                var filled = marketMonitor.CheckOrder(Trade.CurrentTransaction.Order.ClientOrderId);
                if (filled)
                {
                    Trade.Open = false;
                    //stop monitor??
                    marketMonitor.StopStream();
                    Dispose();
                }
            }
        }

        private void Dispose()
        {
            marketMonitor.Dispose();
            Tracker.Dispose();
        }

        private void CreateNewStopLimitOrder()
        {
            Trade.CreateStopLimitTransaction(Tracker.StopLimitPrice);

            IMarketRequest request = new StopLimitRequest(Trade.CurrentTransaction);
            MessageBroker.Instance.Publish(Trade.CurrentTransaction, request);
        }

        public bool Live => Trade.Open;

        public string Symbol => Trade.Symbol;

        public void CancelLimitOrder(string order)
        {
            Trade.CancelCurrentTransaction();
            UpdateStopLimit();
        }

        public void UpdateInitialTransaction(Order order)
        {
            Trade.UpdateCurrentTransaction(order);
            if (order.Status == OrderStatus.Filled)
            {
                Tracker.Configure(order);
            }
        }

        private void UpdateStopLimit()
        {
            Tracker.MoveStopLimit();
            CreateNewStopLimitOrder();
        }

        public void StartStopLossMonitor(Order order)
        {
            Trade.UpdateCurrentTransaction(order);
            //Start the stop limit monitor.
            marketMonitor.StartStream();
        }

        public void AddTrade(ITrade trade)
        {
            Trade = trade;
            marketMonitor.Symbol = trade.Symbol;
            marketMonitor.Subscribe(ProcessCandleStick);
        }
    }
}
