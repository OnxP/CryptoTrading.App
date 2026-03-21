using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database.StoreTrades;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.Monitor
{
    //Monitors a Trade, and manages transactions.
    //Each transaction is linked to an order.
    //The trade is considered to be live if there are open transactions
    public class TradeMonitor : ITradeMonitor
    {
        private ILogger<TradeMonitor> Logger { get; set; }
        private IConfig Config { get; set; }
        public TradeMonitor(ILogger<TradeMonitor>logger,IMarketMonitor monitor,IConfig config)
        {
            marketMonitor = monitor;
            Logger = logger;
            Config = config;
            _quoteHub = new QuoteHub<IQuote>();
        }

        public ITrade Trade => HistoricTrades.Last();

        public List<ITrade> HistoricTrades { get; set; } = new List<ITrade>();
        public decimal CurrentStopLimit { get; set; } 
        public IMarketMonitor marketMonitor { get; set; }
        public DateTime currentCloseTime { get; set; }
        public ITradeRequest Request { get; set; }
        private readonly QuoteHub<IQuote> _quoteHub;
        private PositionState _positionState = PositionState.NoPosition;

        public async Task SubscribetToMarketData()
        {
            if (!marketMonitor.IsSubscribed(Request.Symbol.Ticker, KeyValue))
            {
                var candleSticks = await marketMonitor.GetHistoricCandleSticks(Request.Symbol.Ticker);
                foreach (var candle in candleSticks)
                    _quoteHub.Add(new Quote
                    {
                        Timestamp = candle.CloseTime,
                        Open = candle.Open,
                        High = candle.High,
                        Low = candle.Low,
                        Close = candle.Close,
                        Volume = candle.Volume
                    });
                Request.Strategy.SetQuotes(_quoteHub);
                await marketMonitor.Subscribe(Request.Symbol.Ticker, KeyValue, ProcessCandleStick);
            }
        }
        public async Task SetNewRequest(ITradeRequest what)
        {
            var compareResults = CompareRequests(Request, what);

            switch (compareResults)
            {
                case CompareResults.SameDirection:
                    break;
                case CompareResults.ChangeDirection:
                    //need to exit out of the existing one.
                    if(Trade.GetCurrentTransaction() != null)
                    {
                        await marketMonitor.CheckOrder(Trade.GetCurrentTransaction());
                        if(!Trade.GetCurrentTransaction().IsFilled)
                        {
                            await CancelOrder(Trade.GetCurrentTransaction());
                        }
                        if (Trade.GetCurrentTransaction().IsFilled || Trade.GetCurrentTransaction().IsPartiallyFilled)
                        {
                            //close existing trade
                            var transaction = Trade.CompleteTrade();
                            await SubmitOrder(transaction);
                        }
                    }
                    break;
                default:
                    break;
            }
            Request = what;
            Request.Strategy.SetQuotes(_quoteHub);
        }

        private CompareResults CompareRequests(ITradeRequest request, ITradeRequest what)
        {
            if(request.OrderSide == what.OrderSide)
                return CompareResults.SameDirection;
            else
                return CompareResults.ChangeDirection;
        }

        public async void ProcessCandleStick(ExchangeCandlestickEvent candleStick)
        {
            if (!candleStick.IsFinal) return;
            _quoteHub.Add(new Quote
            {
                Timestamp = candleStick.Candlestick.CloseTime,
                Open = candleStick.Candlestick.Open,
                High = candleStick.Candlestick.High,
                Low = candleStick.Candlestick.Low,
                Close = candleStick.Candlestick.Close,
                Volume = candleStick.Candlestick.Volume
            });

            //only get pending transactions
            if (Trade.PendingEntryTransactions.Count >0  && Trade.PendingExitTransactions.Count >0)
                await marketMonitor.CheckOrder(Trade.GetCurrentTransaction());

            var strategyResult = Request.Strategy.ProcessStrategy(Trade);

            switch (_positionState)
            {
                case PositionState.NoPosition:
                    await HandleNoPosition(strategyResult, candleStick);
                    break;

                case PositionState.Building:
                    await HandleBuildingPosition(strategyResult, candleStick);
                    break;

                case PositionState.FullyOpen:
                    await HandleFullyOpenPosition(strategyResult, candleStick);
                    break;

                case PositionState.InProfit:
                    await HandleInProfitPosition(strategyResult, candleStick);
                    break;

                case PositionState.Closing:
                    await HandleClosingPosition(strategyResult, candleStick);
                    break;
            }
        }

        private async Task HandleNoPosition(StrategyStatus result, ExchangeCandlestickEvent candleStick)
        {
            if (result.StrategyAction == StrategyAction.OpenTrade)
            {
                Logger.LogInformation($"Starting new position for {Symbol}");
                _positionState = PositionState.Building;

                await ExecuteEntryStrategy(candleStick);
            }
        }

        private async Task HandleBuildingPosition(StrategyStatus result, ExchangeCandlestickEvent candleStick)
        {
            // Continue building position using entry strategy
            if (result.StrategyAction == StrategyAction.OpenTrade)
            {
                await ExecuteEntryStrategy(candleStick);
            }
            else if (result.StrategyAction == StrategyAction.CloseTrade)
            {
                // Strategy changed, need to exit early
                Logger.LogInformation($"Exiting position early during build phase for {Symbol}");
                await CancelAllPendingEntries();
                _positionState = PositionState.Closing;
                await ExecuteExitStrategy(candleStick);
            }

            // Check if position is fully built
            if (Trade.RemainingQuantity >= Trade.RemainingQuantity && Trade.PendingEntryTransactions.Count == 0)
            {
                Logger.LogInformation($"Position fully built for {Symbol}. Size: {Trade.RemainingQuantity}");
                _positionState = PositionState.FullyOpen;
            }
        }

        private async Task HandleFullyOpenPosition(StrategyStatus result, ExchangeCandlestickEvent candleStick)
        {
            // Check if position is in profit
            if (Trade.ProfitPct > 0)
            {
                Logger.LogInformation($"Position in profit for {Symbol}. Profit: {Trade.ProfitPct}%");
                _positionState = PositionState.InProfit;
            }

            // Check for exit signal
            if (result.StrategyAction == StrategyAction.CloseTrade)
            {
                Logger.LogInformation($"Exit signal received for {Symbol}");
                _positionState = PositionState.Closing;
                await ExecuteExitStrategy(candleStick);
            }
        }

        private async Task HandleInProfitPosition(StrategyStatus result, ExchangeCandlestickEvent candleStick)
        {
            // Once in profit, use exit strategy
            if (result.StrategyAction == StrategyAction.CloseTrade)
            {
                Logger.LogInformation($"Closing profitable position for {Symbol}. Profit: {Trade.ProfitPct}%");
                _positionState = PositionState.Closing;
                await ExecuteExitStrategy(candleStick);
            }
            else if (Trade.ProfitPct <= 0)
            {
                // Went back below breakeven
                _positionState = PositionState.FullyOpen;
            }
        }

        private async Task HandleClosingPosition(StrategyStatus result, ExchangeCandlestickEvent candleStick)
        {
            // Continue executing exit strategy until position is fully closed
            if (Trade.RemainingQuantity > 0)
            {
                await ExecuteExitStrategy(candleStick);
            }
            else
            {
                Logger.LogInformation($"Position fully closed for {Symbol}");
                _positionState = PositionState.NoPosition;
                CompleteTrade();
            }
        }

        private async Task CancelAllPendingEntries()
        {
            foreach (var transaction in Trade.PendingEntryTransactions.ToList())
            {
                if (!transaction.IsFilled && !transaction.IsPartiallyFilled)
                {
                    await CancelOrder(transaction);
                }
            }
        }

        private async Task ExecuteEntryStrategy(ExchangeCandlestickEvent candleStick)
        {
            // Entry strategy determines how to build the position
            var entryDecision = Request.Strategy.EntryStrategy.GetNextEntry(
                Trade.RemainingQuantity,
                1,
                candleStick.Candlestick.Close
            );

            if (entryDecision.ShouldTrade)
            {
                var transaction = Trade.CreateOpenTransaction(
                    entryDecision.Price,
                    candleStick.Candlestick.CloseTime,
                    entryDecision.Quantity//Quote Quantity
                );
                await SubmitOrder(transaction);

                Logger.LogInformation(
                    $"Entry order placed: {Symbol} Price: {entryDecision.Price}, " +
                    $"Qty: {entryDecision.Quantity}, Type: {entryDecision.OrderType}"
                );
            }
        }

        private async Task ExecuteExitStrategy(ExchangeCandlestickEvent candleStick)
        {
            // Exit strategy determines how to close the position
            var exitDecision = Request.Strategy.ExitStrategy.GetNextExit(
                Trade.RemainingQuantity,
                candleStick.Candlestick.Close,
                Trade.ProfitPct
            );

            if (exitDecision.ShouldTrade)
            {
                var transaction = Trade.CreateCloseTransaction(
                    exitDecision.Price,
                    candleStick.Candlestick.CloseTime,
                    Trade.TotalOpenBaseQuantity
                );

                await SubmitOrder(transaction);

                Logger.LogInformation(
                    $"Exit order placed: {Symbol} Price: {exitDecision.Price}, " +
                    $"Qty: {exitDecision.Quantity}, Type: {exitDecision.OrderType}"
                );
            }
        }

        private async Task SubmitOrder(ITransaction transaction)
        {
            switch(transaction.Type)
            {
                case TransactionType.MarketTransaction:
                    IMarketRequest marketRequest = new MarketRequest(Trade.GetCurrentTransaction());
                    await MessageBroker.Instance.Publish(KeyValue, Trade.GetCurrentTransaction(), marketRequest);
                    break;
                case TransactionType.LimitTransaction:
                    ILimitRequest limitRequest = new LimitRequest(Trade.GetCurrentTransaction());
                    await MessageBroker.Instance.Publish(KeyValue, Trade.GetCurrentTransaction(), limitRequest);
                    break;
                case TransactionType.StopLimitTransaction:
                    IStopLimitRequest stopLimitRequest = new StopLimitRequest(Trade.GetCurrentTransaction());
                    await MessageBroker.Instance.Publish(KeyValue, Trade.GetCurrentTransaction(), stopLimitRequest);
                    break;
            }
        }

        private async Task CancelOrder(ITransaction transaction)
        {
            // FIX: Remove null check for long, just check if Order is not null
            if (transaction.Order != null)
            {
                var orderId = transaction.Order.OrderId;
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
        private IPositions _positions;

        public void AddRequest(ITradeRequest request, IPositions positions)
        {
            Request = request;
            _positions = positions;
            var trade = _positions.CreateTrade(Request);
            HistoricTrades.Add(trade);
            IMessageBroker messageBroker = MessageBroker.Instance;

            Func<MessagePayload<ExchangeOrder>, Task> newTradeMesssage = ProcessMessageAction;
            messageBroker.Subscribe(Request.Symbol.Ticker, newTradeMesssage);

            Func<MessagePayload<string>, Task> CancelTradeMessage = ProcessMessageAction;
            messageBroker.Subscribe(Request.Symbol.Ticker, CancelTradeMessage);
        }

        //Order Placed Message
        private Task ProcessMessageAction(MessagePayload<ExchangeOrder> obj)
        {
            if (obj.Who is ITransaction transaction)
            {
                transaction.UpdateOrder(obj.What);
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
            marketMonitor = null;
            if (Config != null)
            {
                var factory = new ArchiveTradeFactory(Config);
                var trade = factory.CreateHistoricTrades(Trade);
                //StoreTradesToDb(trade, Config);
            }
            var newTrade = _positions.CreateTrade(Request);
            HistoricTrades.Add(newTrade);
        }

        public void AddTrade(ITrade trade)
        {
            HistoricTrades.Add(trade);
        }

        public void CancelLimitOrder(string order)
        {
            Trade.GetCurrentTransaction()?.Cancel();
        }

        public void UpdateInitialTransaction(ExchangeOrder order)
        {
            Trade.UpdateCurrentTransaction(order);
        }

        public void UpdateStopLimitOrder(ExchangeOrder order)
        {
            Trade.UpdateCurrentTransaction(order);
        }
        public static void StoreTradesToDb(HistoricTrades completedTrade, IConfig config)
        {
            if (config != null)
            {
                using var context = new HistoricTradeContext(config.StoreTradesConnectionString);
                context.HistoricTrades.Add(completedTrade);
                context.SaveChanges();
            }
        }

    }
    public enum PositionState
    {
        NoPosition,      // No open position
        Building,        // Building position with entry strategy
        FullyOpen,       // Position fully built but not in profit
        InProfit,        // Position in profit, ready for exit
        Closing          // Executing exit strategy
    }
}
