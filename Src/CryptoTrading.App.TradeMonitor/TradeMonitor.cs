using Binance;
using Binance.Client;
using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Algorithm.RegimeBased.ExitStrategies;
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
            _quoteHub = new QuoteHub<IQuote>(300);
        }

        public ITrade Trade => HistoricTrades.Last();

        public List<ITrade> HistoricTrades { get; set; } = new List<ITrade>();
        public decimal CurrentStopLimit { get; set; } 
        public IMarketMonitor marketMonitor { get; set; }
        public DateTime currentCloseTime { get; set; }
        public ITradeRequest Request { get; set; }
        private readonly QuoteHub<IQuote> _quoteHub;
        private PositionState _positionState = PositionState.NoPosition;
        private ITradeRequest _pendingRequest;
        // When true, the current setup's SL/TP are stale (from a previous trade).
        // No new entries are allowed until a fresh 15M setup arrives via SetNewRequest.
        private bool _setupStale = false;

        public async Task SubscribetToMarketData()
        {
            if (!marketMonitor.IsSubscribed(Request.Symbol, KeyValue))
            {
                var candleSticks = await marketMonitor.GetHistoricCandleSticks(Request.Symbol);
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
                await marketMonitor.Subscribe(Request.Symbol, KeyValue, ProcessCandleStick);
            }
        }
        public async Task SetNewRequest(ITradeRequest what)
        {
            var compareResults = CompareRequests(Request, what);

            switch (compareResults)
            {
                case CompareResults.SameDirection:
                    // Same direction: update the request with fresh setup/prices
                    // but only if we're not currently in an active position
                    if (_positionState == PositionState.NoPosition)
                    {
                        Logger.LogInformation($"Updating setup for {Symbol} (same direction). New entry zone from 15M.");
                        Request = what;
                        Request.Strategy.SetQuotes(_quoteHub);
                        _setupStale = false; // Fresh 15M setup received, allow new entries
                    }
                    else
                    {
                        // Store the new request so CompleteTrade can pick it up after the current trade closes
                        Logger.LogDebug($"In position ({_positionState}) for {Symbol}, queuing new setup for next trade.");
                        _pendingRequest = what;

                        // Also update the current exit strategy's SL/TP with fresh levels
                        // so the active trade uses current risk management, not stale values
                        if (what.Strategy.ExitStrategy is RegimeBasedExitStrategyBase newExit
                            && Request.Strategy.ExitStrategy is RegimeBasedExitStrategyBase currentExit)
                        {
                            currentExit.UpdateSetup(newExit.Setup);
                        }
                    }
                    break;
                case CompareResults.ChangeDirection:
                    //need to exit out of the existing one.
                    if(Trade.GetCurrentTransaction() != null && marketMonitor != null)
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
                    Request = what;
                    Request.Strategy.SetQuotes(_quoteHub);
                    _setupStale = false; // Fresh setup on direction change
                    break;
                default:
                    break;
            }
        }

        private CompareResults CompareRequests(ITradeRequest request, ITradeRequest what)
        {
            if(request.OrderSide == what.OrderSide)
                //if(request.Is)
                return CompareResults.SameDirection;
            else
                return CompareResults.ChangeDirection;
        }

        //for the database it is 1m candles but for live trading it is the live market price.
        //so the candle needs to be final before processing.
        public async void ProcessCandleStick(CandlestickEventArgs candleStick)
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

            var ts = candleStick.Candlestick.CloseTime.ToString("yyyy-MM-dd HH:mm");

            // Reset daily circuit breaker tracking at day boundary
            currentCloseTime = candleStick.Candlestick.CloseTime;

            //only get pending transactions
            if (Trade.PendingEntryTransactions.Count >0  && Trade.PendingExitTransactions.Count >0)
                await marketMonitor.CheckOrder(Trade.GetCurrentTransaction());

            var strategyResult = Request.Strategy.ProcessStrategy(Trade);
            var previousState = _positionState;

            // Log state at Info level when entry/exit signals fire (not just Debug)
            if (strategyResult.StrategyAction != StrategyAction.NoAction)
                Logger.LogInformation($"[1M TM {ts}] {Symbol} State:{_positionState} Action:{strategyResult.StrategyAction} TradeOpen:{Trade.Open} Qty:{Trade.TotalOpenBaseQuantity} Price:{candleStick.Candlestick.Close:F2}");
            else
                Logger.LogDebug($"[1M TM {ts}] {Symbol} State:{_positionState} Action:{strategyResult.StrategyAction} Price:{candleStick.Candlestick.Close:F2} Quotes:{_quoteHub.Quotes.Count}");

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

            if (_positionState != previousState)
                Logger.LogInformation($"[1M TM {ts}] {Symbol} STATE CHANGE: {previousState} -> {_positionState}");

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
        }

        private async Task HandleNoPosition(StrategyStatus result, CandlestickEventArgs candleStick)
        {
            if (result.StrategyAction == StrategyAction.OpenTrade)
            {
                // Note: _setupStale guard removed. After a trade closes, Live=false so
                // TradeProcessor.CheckCurrentOrderMonitors returns false. New 15M setups
                // create a NEW TradeMonitor with fresh SL/TP via TradeFactory.CreateMonitor.
                // The stale flag was blocking ALL entries on this monitor permanently since
                // SetNewRequest only routes to Live monitors.

                try
                {
                    Logger.LogInformation($"Starting new position for {Symbol}");
                    _positionState = PositionState.Building;

                    // Reset exit strategy state for the new trade (BarsHeld, EntryPrice, etc.)
                    Request.Strategy.ExitStrategy?.ResetForNewTrade();

                    await ExecuteEntryStrategy(candleStick);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"[1M TM] Exception in HandleNoPosition for {Symbol}. Resetting to NoPosition.");
                    _positionState = PositionState.NoPosition;
                }
            }
        }

        private async Task HandleBuildingPosition(StrategyStatus result, CandlestickEventArgs candleStick)
        {
            // Continue building position using entry strategy
            if (result.StrategyAction == StrategyAction.OpenTrade)
            {
                try
                {
                    await ExecuteEntryStrategy(candleStick);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"[1M TM] Exception in HandleBuildingPosition for {Symbol}. Resetting to NoPosition.");
                    _positionState = PositionState.NoPosition;
                    return;
                }
            }
            else if (result.StrategyAction == StrategyAction.CloseTrade)
            {
                // Strategy changed, need to exit early
                Logger.LogInformation($"Exiting position early during build phase for {Symbol}");
                await CancelAllPendingEntries();

                // If nothing was filled, go straight back to NoPosition
                if (Trade.TotalOpenBaseQuantity <= 0)
                {
                    Logger.LogInformation($"No filled quantity to close for {Symbol}. Returning to NoPosition");
                    _positionState = PositionState.NoPosition;
                    CompleteTrade();
                    return;
                }

                _positionState = PositionState.Closing;
                await ExecuteExitStrategy(candleStick, result.ExitDetails);
                return; // Don't fall through to the FullyOpen check
            }

            // Safety: if Building but nothing filled and no pending entries, reset to NoPosition.
            // This prevents getting stuck in Building state when entries repeatedly fail.
            if (Trade.TotalOpenBaseQuantity <= 0 && Trade.PendingEntryTransactions.Count == 0)
            {
                Logger.LogInformation($"[1M TM] Building with no entries for {Symbol} — resetting to NoPosition");
                _positionState = PositionState.NoPosition;
                return;
            }

            // Check if position is fully built (has filled quantity and no pending entries)
            if (Trade.TotalOpenBaseQuantity > 0 && Trade.PendingEntryTransactions.Count == 0)
            {
                Logger.LogInformation($"Position fully built for {Symbol}. Size: {Trade.TotalOpenBaseQuantity}");
                _positionState = PositionState.FullyOpen;
            }
        }

        private async Task HandleFullyOpenPosition(StrategyStatus result, CandlestickEventArgs candleStick)
        {
            // Safety check: if position has no quantity, reset to NoPosition
            if (Trade.TotalOpenBaseQuantity <= 0)
            {
                Logger.LogWarning($"Position has zero quantity for {Symbol}. Resetting to NoPosition");
                _positionState = PositionState.NoPosition;
                CompleteTrade();
                return;
            }

            // Check for exit signal first (takes priority)
            if (result.StrategyAction == StrategyAction.CloseTrade)
            {
                Logger.LogInformation($"Exit signal received for {Symbol}");
                _positionState = PositionState.Closing;
                await ExecuteExitStrategy(candleStick, result.ExitDetails);
                return;
            }

            // Check if position is in profit
            if (Trade.ProfitPct > 0)
            {
                Logger.LogInformation($"Position in profit for {Symbol}. Profit: {Trade.ProfitPct}%");
                _positionState = PositionState.InProfit;
            }
        }

        private async Task HandleInProfitPosition(StrategyStatus result, CandlestickEventArgs candleStick)
        {
            // Safety check: if position has no quantity, reset to NoPosition
            if (Trade.TotalOpenBaseQuantity <= 0)
            {
                Logger.LogWarning($"InProfit position has zero quantity for {Symbol}. Resetting to NoPosition");
                _positionState = PositionState.NoPosition;
                CompleteTrade();
                return;
            }

            // Once in profit, use exit strategy
            if (result.StrategyAction == StrategyAction.CloseTrade)
            {
                Logger.LogInformation($"Closing profitable position for {Symbol}. Profit: {Trade.ProfitPct}%");
                _positionState = PositionState.Closing;
                await ExecuteExitStrategy(candleStick, result.ExitDetails);
            }
            else if (Trade.ProfitPct <= 0)
            {
                // Went back below breakeven
                _positionState = PositionState.FullyOpen;
            }
        }

        private async Task HandleClosingPosition(StrategyStatus result, CandlestickEventArgs candleStick)
        {
            // Continue executing exit strategy until position is fully closed
            if (Trade.RemainingQuantity > 0 && Trade.TotalOpenBaseQuantity > 0)
            {
                // After a partial exit, return to active monitoring (not closing)
                // so the ScaleOut strategy can fire subsequent partials at 2R, 3R etc.
                if (Trade.TotalOpenBaseQuantity > 0 && Trade.PendingExitTransactions.Count == 0)
                {
                    Logger.LogInformation($"Partial exit complete for {Symbol}. Remaining: {Trade.TotalOpenBaseQuantity}. Returning to active monitoring.");
                    _positionState = Trade.ProfitPct > 0 ? PositionState.InProfit : PositionState.FullyOpen;
                    return;
                }
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

        private async Task ExecuteEntryStrategy(CandlestickEventArgs candleStick)
        {
            // Entry strategy determines how to build the position
            var entryDecision = Request.Strategy.EntryStrategy.GetNextEntry(
                Trade.RemainingQuantity,
                1,
                candleStick.Candlestick.Close
            );

            if (entryDecision.ShouldTrade)
            {
                // For backtest LIMIT orders: a limit buy fills at min(limit, market),
                // a limit sell fills at max(limit, market). The exchange would never
                // fill worse than the current market price.
                var marketPrice = candleStick.Candlestick.Close;
                var fillPrice = string.Equals(entryDecision.OrderType?.ToString(), "LIMIT", StringComparison.OrdinalIgnoreCase)
                    ? (Request.OrderSide == Binance.OrderSide.Buy
                        ? Math.Min(entryDecision.Price, marketPrice)
                        : Math.Max(entryDecision.Price, marketPrice))
                    : marketPrice;

                if (fillPrice != entryDecision.Price)
                {
                    Logger.LogInformation(
                        $"Entry LIMIT price adjusted: {entryDecision.Price:F2} -> {fillPrice:F2} (market: {marketPrice:F2})");
                }

                var transaction = Trade.CreateOpenTransaction(
                    fillPrice,
                    candleStick.Candlestick.CloseTime,
                    entryDecision.Quantity//Quote Quantity
                );
                await SubmitOrder(transaction);

                Logger.LogInformation(
                    $"Entry order placed: {Symbol} Price: {fillPrice}, " +
                    $"Qty: {entryDecision.Quantity}, Type: {entryDecision.OrderType}"
                );
            }
        }

        private async Task ExecuteExitStrategy(CandlestickEventArgs candleStick, TradeDetails cachedExit = null)
        {
            // Use the cached exit decision from ProcessStrategy if available,
            // to avoid calling GetNextExit twice (which double-increments BarsHeld).
            var exitDecision = cachedExit?.ShouldTrade == true
                ? cachedExit
                : Request.Strategy.ExitStrategy.GetNextExit(
                    Trade.RemainingQuantity,
                    candleStick.Candlestick.Close,
                    Trade.ProfitPct
                );

            if (exitDecision.ShouldTrade)
            {
                // Use the exit decision's quantity for partial exits (ScaleOut strategy),
                // capped at the total open quantity to prevent over-selling.
                var exitQty = exitDecision.Quantity > 0 && exitDecision.Quantity < Trade.TotalOpenBaseQuantity
                    ? exitDecision.Quantity
                    : Trade.TotalOpenBaseQuantity;

                var transaction = Trade.CreateCloseTransaction(
                    exitDecision.Price,
                    candleStick.Candlestick.CloseTime,
                    exitQty
                );

                await SubmitOrder(transaction);

                Logger.LogInformation(
                    $"Exit order placed: {Symbol} Price: {exitDecision.Price}, " +
                    $"Qty: {exitQty} (requested: {exitDecision.Quantity}), Type: {exitDecision.OrderType}"
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
        private IPositions _positions;

        public void AddRequest(ITradeRequest request, IPositions positions)
        {
            Request = request;
            _positions = positions;
            var trade = _positions.CreateTrade(Request);
            HistoricTrades.Add(trade);
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
            // Note: marketMonitor is intentionally kept alive here so the monitor
            // can continue processing candles and handling new requests for the next trade.
            if (Config != null)
            {
                var factory = new ArchiveTradeFactory(Config);
                var trade = factory.CreateHistoricTrades(Trade);
                //StoreTradesToDb(trade, Config);
            }

            // Update performance tracker with trade P&L for anti-martingale sizing and circuit breaker
            try
            {
                var pnl = Trade.Profit;
                if (Request?.Strategy is RegimeBasedExecutionStrategy execStrategy)
                {
                    // Try to reach the performance tracker through the algorithm chain
                    // The tracker is updated so the next setup evaluation uses correct sizing
                    Logger.LogDebug($"[1M TM] Trade completed for {Symbol}. PnL: {pnl:F4}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, $"[1M TM] Non-critical: failed to update performance tracker");
            }

            // Adopt the pending request if a newer setup arrived while in position
            if (_pendingRequest != null)
            {
                Logger.LogInformation($"Adopting queued setup for {Symbol} with fresh prices.");
                Request = _pendingRequest;
                Request.Strategy.SetQuotes(_quoteHub);
                _pendingRequest = null;
                _setupStale = false; // Fresh setup adopted
            }
            else
            {
                // No fresh setup available — mark as stale so we don't enter
                // with SL/TP calculated for a different market price.
                _setupStale = true;
                Logger.LogDebug($"[1M TM] No pending setup for {Symbol} after trade close. Marking setup as stale.");
            }

            // Reset exit strategy state for the next trade
            Request.Strategy.ExitStrategy?.ResetForNewTrade();

            var newTrade = _positions.CreateTrade(Request);
            HistoricTrades.Add(newTrade);
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
