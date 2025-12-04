using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Database.StoreTrades;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

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
        public DateTime currentCloseTime { get; set; }
        public ITradeRequest Request { get; set; }

        public async Task SubscribetToMarketData()
        {
            if (!marketMonitor.IsSubscribed(Request.Symbol, KeyValue))
            {
                var candleSticks = await marketMonitor.GetHistoricCandleSticks(Request.Symbol);
                Request.Strategy.LoadHistoricCandleSticks(candleSticks);
                await marketMonitor.Subscribe(Request.Symbol, KeyValue, ProcessCandleStick);
            }
        }
        public void SetNewRequest(ITradeRequest what)
        {
            Request = what;

            //need to decide what to do here. if some clean up is needed Process next candle stick should take care of this,
            //but cancelling an open order when the trade needs to flip or exiting out of an existing Buy due to a new sell order 
        }

        //for the database it is 1m candles but for live trading it is the live market price.
        //so the candle needs to be final before processing.
        public async void ProcessCandleStick(CandlestickEventArgs candleStick)
        {
            if (!candleStick.IsFinal) return;
            var result = Request.Strategy.ProcessCandleStick(candleStick);

            if(Trade.CurrentTransaction != null && !Trade.CurrentTransaction.IsFilled)
                await marketMonitor.CheckOrder(Trade.CurrentTransaction);
            //3 options Open/Move position, Exit position, Hold position.

            ///several options or flows here
            ///strategy want to open a new position
            ///strategy wants to open a new position but there is already one open.-> move limit order (assume that its partially filled)
            ///Strategy wants to close position -> cancel limit order and exit position.
            ///Strategy wants to hold position -> do nothing.
            ///Strategy changed what to do then??
            /// - and there is no position -> open position
            /// - and there is a un filled position -> cancel and open a new one.
            /// - and there is a filled position -> we may need to exit depending on the strategy
            ///     need the best way to decide what to do here 
            ///         - leave it alone
            ///         - exit position at profit then open a new one.
            ///         - exit position at a loss then open a new one. (assuming long short filp) do we wait for it to go into profit first? 
            ///             leave it open for now and close once the open order is ready, hopefully this situation does not arise often.


            // Replace the empty switch block in ProcessCandleStick with cases for each StrategyAction
            var price = Request.Strategy.GetEntryPrice(candleStick);
            switch (result.StrategyAction)
            {
                case StrategyAction.OpenTrade:

                    ///strategy want to open a new position
                    ///strategy wants to open a new position but there is already one open.-> move limit order (assume that its partially filled)
                    ///Strategy changed - and there is a un filled position -> cancel and open a new one.
                    if (Trade.CurrentTransaction == null || Trade.CurrentTransaction != null && Trade.CurrentTransaction.IsFilled)
                    {
                        var transaction = Trade.CreateNewTransaction(price, candleStick.Candlestick.CloseTime, Request.Strategy);
                        //this needs to update the transaction once the order is filled.
                        //trasnaction should have all the info for the order.
                        //order is async as it is fire and forget. once the order hits the exchange we continue.
                        await SubmitOrder(transaction);
                    }
                    else if (Trade.CurrentTransaction != null && !Trade.CurrentTransaction.IsFilled)
                    {
                        //cancel existing order and create a new one.
                        //check to see if the price has changed, if not then do nothing.

                        //need to check on the order.
                        
                        if (Trade.CurrentTransaction.Price != price && !Trade.CurrentTransaction.IsFilled)
                        {
                            await CancelOrder(Trade.CurrentTransaction);
                            var transaction = Trade.CreateNewTransaction(price, candleStick.Candlestick.CloseTime, Request.Strategy);
                            await SubmitOrder(transaction);
                        }
                    }
                    break;
                case StrategyAction.NoAction:
                    ///Strategy wants to hold position -> do nothing.
                    // Hold position, do nothing
                    break;
            }

            


            //var closePrice = candleStick.Candlestick.Close;
            //Trade.CurrentPrice = closePrice;
            //currentCloseTime = candleStick.Candlestick.CloseTime;
            //Tracker.CurrentPrice = closePrice;
            ////Logger.LogInformation($"Process CandleStick {Symbol} at {currentCloseTime} PRICE: {closePrice} - Target {Tracker.TargetPrice} : Stop Limit: {Tracker.StopLimitPrice}");
            //if (Tracker.RequestUpdateOfStopLimit(candleStick.Candlestick.High))
            //{
            //    UpdateStopLimit(Tracker.TargetPrice< candleStick.Candlestick.High);
            //}
            //if (candleStick.Candlestick.Low <= Tracker.StopLimitPrice)
            //{
            //    DbCandleStickManagement.PauseFlow = true;
            //    //check for fill order
            //    if (await marketMonitor.CheckOrder(Trade.CurrentTransaction))
            //    {
            //        Trade.CurrentTransaction.TransactionDate = currentCloseTime;
            //        Tracker.EndDateTime = currentCloseTime;
            //        Trade.Open = false;
                    
            //        //unsubscribe to monitor
            //        marketMonitor.UnSubscribe(candleStick.Candlestick.Symbol, KeyValue);
            //        Tracker.IsOpen = false;
            //        Logger.LogInformation($"Completed Trade {Trade.Pair} finalPrice {Trade.CurrentTransaction.Price} profit {(Trade.Profit/100):P}");
            //        if(Config != null)
            //        {
            //            var factory = new ArchiveTradeFactory(Config);
            //            var trade = factory.CreateHistoricTrades(Trade);
            //            StoreTradesToDb(trade, Config);
            //        }
            //        Dispose();
                    //DbCandleStickManagement.PauseFlow = false;
            //        return;
            //    }
            //    else
            //    {
            //        //partial fill of the order. should just continue...
            //    }
                
            //}

            
            //DbCandleStickManagement.PauseFlow = false;
        }

        private async Task SubmitOrder(ITransaction transaction)
        {
            switch(transaction.Type)
            {
                case TransactionType.MarketTransaction:
                    IMarketRequest marketRequest = new MarketRequest(Trade.CurrentTransaction);
                    await MessageBroker.Instance.Publish(KeyValue, Trade.CurrentTransaction, marketRequest);
                    break;
                case TransactionType.LimitTransaction:
                    ILimitRequest limitRequest = new LimitRequest(Trade.CurrentTransaction);
                    await MessageBroker.Instance.Publish(KeyValue, Trade.CurrentTransaction, limitRequest);
                    break;
                case TransactionType.StopLimitTransaction:
                    IStopLimitRequest stopLimitRequest = new StopLimitRequest(Trade.CurrentTransaction);
                    await MessageBroker.Instance.Publish(KeyValue, Trade.CurrentTransaction, stopLimitRequest);
                    break;
            }
        }

        //public static void StoreTradesToDb(HistoricTrades completedTrade, IConfig config)
        //{
        //    using var context = new HistoricTradeContext(config.StoreTradesConnectionString);
        //    context.HistoricTrades.Add(completedTrade);
        //    context.SaveChanges();
        //}

        private void Dispose()
        {
            Trade.CurrentTransaction.Complete();
            marketMonitor = null;
        }


        private async Task CancelOrder(ITransaction transaction)
        {
            // FIX: Remove null check for long, just check if Order is not null
            if (Trade.CurrentTransaction.Order != null)
            {
                var orderId = transaction.Order.Id;
                ICancelRequest request = new CancelRequest(orderId, Trade.Pair);
                await MessageBroker.Instance.Publish(KeyValue, transaction, request);
            }
        }

        public bool Live => Trade.Open;
        public override string ToString()
        {
            return $"{Symbol} - {currentCloseTime:s}";
        }

        public string Symbol => Trade.Pair;

        public string KeyValue { get; set; } = "1";

        public void AddRequest(ITradeRequest request, IPositions positions)
        {
            Request = request;
            Trade = positions.CreateTrade(Request);
            IMessageBroker messageBroker = MessageBroker.Instance;

            Func<MessagePayload<Order>, Task> newTradeMesssage = ProcessMessageAction;
            messageBroker.Subscribe(Request.Symbol, newTradeMesssage);

            Func<MessagePayload<string>, Task> CancelTradeMessage = ProcessMessageAction;
            messageBroker.Subscribe(Request.Symbol, CancelTradeMessage);
        }

        //Order Placed Message
        private Task ProcessMessageAction(MessagePayload<Order> obj)
        {
            if (obj.Who is ITransaction transaction)
            {
                Order order = obj.What;
                transaction.UpdateOrder(order);

            }
            return Task.CompletedTask;
        }

        //Cancel Order Message
        private Task ProcessMessageAction(MessagePayload<string> obj)
        {
            if (obj.Who is ITransaction transaction)
            {
                transaction.Cancel();
            }
            return Task.CompletedTask;
        }

        public void CompleteTrade()
        {
            Trade.CurrentTransaction.Complete();
            Trade.CompleteTrade();
            marketMonitor = null;
        }

        
    }
}
