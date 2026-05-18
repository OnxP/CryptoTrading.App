using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Monitor.Position;
using CryptoTrading.App.Monitor.Strategies;
using CryptoTrading.App.Monitor.Strategies.RegimeBased.ExitStrategies;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoTrading.App.Monitor
{
    public class TradeMonitor : ITradeMonitor
    {
        private ILogger<TradeMonitor> Logger { get; set; }
        private IConfig Config { get; set; }
        private readonly IBroker _broker;
        private readonly StrategySelector _strategySelector;

        private readonly PositionTracker _position = new();
        public List<HistoricTradeRecord> CompletedTrades { get; } = new();

        public ITradeSignal Signal { get; set; }
        private ITradeSignal _pendingSignal;
        private IExecutionStrategy _strategy;
        private ExchangeOrder _pendingOrder;

        public IMarketMonitor marketMonitor { get; set; }
        public DateTime currentCloseTime { get; set; }
        public decimal CurrentStopLimit { get; set; }
        private readonly QuoteHub<IQuote> _quoteHub;

        public TradeMonitor(ILogger<TradeMonitor> logger, IMarketMonitor monitor, IConfig config, IBroker broker)
        {
            marketMonitor = monitor;
            Logger = logger;
            Config = config;
            _broker = broker;
            _quoteHub = new QuoteHub<IQuote>(300);
            _strategySelector = new StrategySelector(logger);
        }

        public bool Live => _position.IsOpen;
        public string Symbol => Signal?.Symbol;
        public string KeyValue { get; set; } = "1";

        public void AcceptSignal(ITradeSignal signal)
        {
            Signal = signal;
            _position.Symbol = signal.Symbol;
            _position.Direction = signal.Direction;
            _position.TargetQty = signal.Quantity;
            _strategy = _strategySelector.CreateStrategy(signal, _quoteHub);
        }

        public async Task SubscribetToMarketData()
        {
            if (!marketMonitor.IsSubscribed(Signal.Symbol, KeyValue))
            {
                var candleSticks = await marketMonitor.GetHistoricCandleSticks(Signal.Symbol);
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
                _strategy.SetQuotes(_quoteHub);
                await marketMonitor.Subscribe(Signal.Symbol, KeyValue, ProcessCandleStick);
            }
        }

        public async Task SetNewSignal(ITradeSignal signal)
        {
            var compareResults = CompareSignals(Signal, signal);

            switch (compareResults)
            {
                case CompareResults.SameDirection:
                    if (_position.State == PositionState.NoPosition)
                    {
                        Logger.LogInformation($"Updating setup for {Symbol} (same direction). New entry zone from 15M.");
                        Signal = signal;
                        _strategy = _strategySelector.CreateStrategy(signal, _quoteHub);
                    }
                    else
                    {
                        Logger.LogDebug($"In position ({_position.State}) for {Symbol}, queuing new setup for next trade.");
                        _pendingSignal = signal;

                        var newStrategy = _strategySelector.CreateStrategy(signal, _quoteHub);
                        if (newStrategy.ExitStrategy is RegimeBasedExitStrategyBase newExit
                            && _strategy.ExitStrategy is RegimeBasedExitStrategyBase currentExit)
                        {
                            currentExit.UpdateSetup(newExit.Setup);
                        }
                    }
                    break;
                case CompareResults.ChangeDirection:
                    if (_position.IsOpen && marketMonitor != null)
                    {
                        if (_pendingOrder != null)
                        {
                            var orderId = _pendingOrder.OrderId ?? _pendingOrder.ClientOrderId;
                            var updatedOrder = await marketMonitor.CheckOrder(orderId, Signal.Symbol);
                            if (updatedOrder != null && updatedOrder.FilledQuantity > _pendingOrder.FilledQuantity)
                            {
                                var delta = updatedOrder.FilledQuantity - _pendingOrder.FilledQuantity;
                                _position.RecordFill(new ExchangeOrder
                                {
                                    FilledQuantity = delta,
                                    Price = updatedOrder.Price,
                                    Timestamp = updatedOrder.Timestamp
                                });
                            }

                            if (_pendingOrder.Status == ExchangeOrderStatus.New)
                            {
                                await _broker.CancelOrder(Signal.Symbol, _pendingOrder.OrderId);
                            }
                            _pendingOrder = null;
                        }

                        if (_position.RemainingQty > 0)
                        {
                            var side = _position.Direction == TradeDirection.Long
                                ? ExchangeOrderSide.Sell : ExchangeOrderSide.Buy;
                            var order = await _broker.SubmitMarketOrder(Signal.Symbol, side, _position.RemainingQty);
                            _position.RecordExit(order);
                        }

                        CompleteTrade();
                    }
                    Signal = signal;
                    _strategy = _strategySelector.CreateStrategy(signal, _quoteHub);
                    _position.Symbol = signal.Symbol;
                    _position.Direction = signal.Direction;
                    _position.TargetQty = signal.Quantity;
                    break;
            }
        }

        private CompareResults CompareSignals(ITradeSignal current, ITradeSignal incoming)
        {
            if (current.Direction == incoming.Direction)
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

            var ts = candleStick.Candlestick.CloseTime.ToString("yyyy-MM-dd HH:mm");
            var close = candleStick.Candlestick.Close;
            currentCloseTime = candleStick.Candlestick.CloseTime;

            if (_pendingOrder != null)
            {
                var orderId = _pendingOrder.OrderId ?? _pendingOrder.ClientOrderId;
                var updatedOrder = await marketMonitor.CheckOrder(orderId, Signal.Symbol);
                if (updatedOrder != null)
                {
                    if (updatedOrder.FilledQuantity > _pendingOrder.FilledQuantity)
                    {
                        var delta = updatedOrder.FilledQuantity - _pendingOrder.FilledQuantity;
                        var fillOrder = new ExchangeOrder
                        {
                            FilledQuantity = delta,
                            Price = updatedOrder.Price,
                            Timestamp = updatedOrder.Timestamp
                        };

                        if (_position.State == PositionState.Building)
                            _position.RecordFill(fillOrder);
                        else if (_position.State == PositionState.Closing)
                            _position.RecordExit(fillOrder);
                    }

                    if (updatedOrder.Status != ExchangeOrderStatus.New
                        && updatedOrder.Status != ExchangeOrderStatus.PartiallyFilled)
                        _pendingOrder = null;
                    else
                        _pendingOrder = updatedOrder;
                }
            }

            var tradeState = new TradeState(
                _position.IsOpen,
                _position.ProfitPct(close),
                _position.FilledQty,
                _position.RemainingQty);
            var strategyResult = _strategy.ProcessStrategy(tradeState);
            var previousState = _position.State;

            if (strategyResult.StrategyAction != StrategyAction.NoAction)
                Logger.LogInformation($"[1M TM {ts}] {Symbol} State:{_position.State} Action:{strategyResult.StrategyAction} TradeOpen:{_position.IsOpen} Qty:{_position.FilledQty} Price:{close:F2}");
            else
                Logger.LogDebug($"[1M TM {ts}] {Symbol} State:{_position.State} Action:{strategyResult.StrategyAction} Price:{close:F2} Quotes:{_quoteHub.Quotes.Count}");

            switch (_position.State)
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

            if (_position.State != previousState)
                Logger.LogInformation($"[1M TM {ts}] {Symbol} STATE CHANGE: {previousState} -> {_position.State}");
        }

        private async Task HandleNoPosition(StrategyStatus result, ExchangeCandlestickEvent candleStick)
        {
            if (result.StrategyAction == StrategyAction.OpenTrade)
            {
                try
                {
                    Logger.LogInformation($"Starting new position for {Symbol}");
                    _position.State = PositionState.Building;
                    _strategy.ExitStrategy?.ResetForNewTrade();
                    await ExecuteEntryStrategy(candleStick);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"[1M TM] Exception in HandleNoPosition for {Symbol}. Resetting to NoPosition.");
                    _position.State = PositionState.NoPosition;
                }
            }
        }

        private async Task HandleBuildingPosition(StrategyStatus result, ExchangeCandlestickEvent candleStick)
        {
            if (result.StrategyAction == StrategyAction.OpenTrade)
            {
                try
                {
                    await ExecuteEntryStrategy(candleStick);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"[1M TM] Exception in HandleBuildingPosition for {Symbol}. Resetting to NoPosition.");
                    _position.State = PositionState.NoPosition;
                    return;
                }
            }
            else if (result.StrategyAction == StrategyAction.CloseTrade)
            {
                Logger.LogInformation($"Exiting position early during build phase for {Symbol}");

                if (_pendingOrder != null)
                {
                    await _broker.CancelOrder(Signal.Symbol, _pendingOrder.OrderId);
                    _pendingOrder = null;
                }

                if (_position.FilledQty <= 0)
                {
                    Logger.LogInformation($"No filled quantity to close for {Symbol}. Returning to NoPosition");
                    _position.State = PositionState.NoPosition;
                    CompleteTrade();
                    return;
                }

                _position.State = PositionState.Closing;
                await ExecuteExitStrategy(candleStick, result.ExitDetails);
                return;
            }

            if (_position.FilledQty <= 0 && _pendingOrder == null)
            {
                Logger.LogInformation($"[1M TM] Building with no entries for {Symbol} — resetting to NoPosition");
                _position.State = PositionState.NoPosition;
                return;
            }

            if (_position.FilledQty > 0 && _pendingOrder == null)
            {
                Logger.LogInformation($"Position fully built for {Symbol}. Size: {_position.FilledQty}");
                _position.State = PositionState.FullyOpen;
            }
        }

        private async Task HandleFullyOpenPosition(StrategyStatus result, ExchangeCandlestickEvent candleStick)
        {
            var close = candleStick.Candlestick.Close;

            if (_position.FilledQty <= 0)
            {
                Logger.LogWarning($"Position has zero quantity for {Symbol}. Resetting to NoPosition");
                _position.State = PositionState.NoPosition;
                CompleteTrade();
                return;
            }

            if (result.StrategyAction == StrategyAction.CloseTrade)
            {
                Logger.LogInformation($"Exit signal received for {Symbol}");
                _position.State = PositionState.Closing;
                await ExecuteExitStrategy(candleStick, result.ExitDetails);
                return;
            }

            if (_position.ProfitPct(close) > 0)
            {
                Logger.LogInformation($"Position in profit for {Symbol}. Profit: {_position.ProfitPct(close)}%");
                _position.State = PositionState.InProfit;
            }
        }

        private async Task HandleInProfitPosition(StrategyStatus result, ExchangeCandlestickEvent candleStick)
        {
            var close = candleStick.Candlestick.Close;

            if (_position.FilledQty <= 0)
            {
                Logger.LogWarning($"InProfit position has zero quantity for {Symbol}. Resetting to NoPosition");
                _position.State = PositionState.NoPosition;
                CompleteTrade();
                return;
            }

            if (result.StrategyAction == StrategyAction.CloseTrade)
            {
                Logger.LogInformation($"Closing profitable position for {Symbol}. Profit: {_position.ProfitPct(close)}%");
                _position.State = PositionState.Closing;
                await ExecuteExitStrategy(candleStick, result.ExitDetails);
            }
            else if (_position.ProfitPct(close) <= 0)
            {
                _position.State = PositionState.FullyOpen;
            }
        }

        private async Task HandleClosingPosition(StrategyStatus result, ExchangeCandlestickEvent candleStick)
        {
            var close = candleStick.Candlestick.Close;

            if (_position.RemainingQty > 0 && _position.FilledQty > 0)
            {
                if (_position.RemainingQty > 0 && _pendingOrder == null)
                {
                    Logger.LogInformation($"Partial exit complete for {Symbol}. Remaining: {_position.RemainingQty}. Returning to active monitoring.");
                    _position.State = _position.ProfitPct(close) > 0
                        ? PositionState.InProfit : PositionState.FullyOpen;
                    return;
                }
                await ExecuteExitStrategy(candleStick);
            }
            else
            {
                Logger.LogInformation($"Position fully closed for {Symbol}");
                _position.State = PositionState.NoPosition;
                CompleteTrade();
            }
        }

        private async Task ExecuteEntryStrategy(ExchangeCandlestickEvent candleStick)
        {
            var targetQty = _strategy.Quantity > 0 ? _strategy.Quantity : 0.1m;
            var entryDecision = _strategy.EntryStrategy.GetNextEntry(
                _position.FilledQty,
                targetQty,
                candleStick.Candlestick.Close
            );

            if (entryDecision.ShouldTrade)
            {
                var orderSide = Signal.Direction == TradeDirection.Long
                    ? ExchangeOrderSide.Buy : ExchangeOrderSide.Sell;
                var marketPrice = candleStick.Candlestick.Close;
                var isLimit = string.Equals(entryDecision.OrderType?.ToString(), "LIMIT", StringComparison.OrdinalIgnoreCase);
                var fillPrice = isLimit
                    ? (orderSide == ExchangeOrderSide.Buy
                        ? Math.Min(entryDecision.Price, marketPrice)
                        : Math.Max(entryDecision.Price, marketPrice))
                    : marketPrice;

                if (fillPrice != entryDecision.Price && isLimit)
                {
                    Logger.LogInformation(
                        $"Entry LIMIT price adjusted: {entryDecision.Price:F2} -> {fillPrice:F2} (market: {marketPrice:F2})");
                }

                ExchangeOrder order;
                if (isLimit)
                    order = await _broker.SubmitLimitOrder(Signal.Symbol, orderSide, entryDecision.Quantity, fillPrice);
                else
                    order = await _broker.SubmitMarketOrder(Signal.Symbol, orderSide, entryDecision.Quantity);

                if (order.FilledQuantity > 0)
                    _position.RecordFill(order);

                if (order.Status == ExchangeOrderStatus.New || order.Status == ExchangeOrderStatus.PartiallyFilled)
                    _pendingOrder = order;

                Logger.LogInformation(
                    $"Entry order placed: {Symbol} Price: {fillPrice}, " +
                    $"Qty: {entryDecision.Quantity:F6} BTC, Type: {entryDecision.OrderType}");
            }
        }

        private async Task ExecuteExitStrategy(ExchangeCandlestickEvent candleStick, TradeDetails cachedExit = null)
        {
            var close = candleStick.Candlestick.Close;
            var exitDecision = cachedExit?.ShouldTrade == true
                ? cachedExit
                : _strategy.ExitStrategy.GetNextExit(
                    _position.RemainingQty,
                    close,
                    _position.ProfitPct(close));

            if (exitDecision.ShouldTrade)
            {
                var exitQty = exitDecision.Quantity > 0 && exitDecision.Quantity < _position.RemainingQty
                    ? exitDecision.Quantity
                    : _position.RemainingQty;

                var exitSide = _position.Direction == TradeDirection.Long
                    ? ExchangeOrderSide.Sell : ExchangeOrderSide.Buy;
                var order = await _broker.SubmitMarketOrder(Signal.Symbol, exitSide, exitQty);

                if (order.FilledQuantity > 0)
                    _position.RecordExit(order);

                if (order.Status == ExchangeOrderStatus.New || order.Status == ExchangeOrderStatus.PartiallyFilled)
                    _pendingOrder = order;

                Logger.LogInformation(
                    $"Exit order placed: {Symbol} Price: {exitDecision.Price}, " +
                    $"Qty: {exitQty} (requested: {exitDecision.Quantity}), Type: {exitDecision.OrderType}");
            }
        }

        public override string ToString()
        {
            return $"{Symbol} - {currentCloseTime:s}";
        }

        public void CompleteTrade()
        {
            var historicTrade = _position.ToHistoricTrade();
            CompletedTrades.Add(historicTrade);

            Logger.LogDebug($"[1M TM] Trade completed for {Symbol}. PnL: {historicTrade.Profit:F4}");

            if (_pendingSignal != null)
            {
                Logger.LogInformation($"Adopting queued setup for {Symbol} with fresh prices.");
                Signal = _pendingSignal;
                _strategy = _strategySelector.CreateStrategy(_pendingSignal, _quoteHub);
                _pendingSignal = null;
            }
            else
            {
                Logger.LogDebug($"[1M TM] No pending setup for {Symbol} after trade close.");
            }

            _strategy.ExitStrategy?.ResetForNewTrade();

            _position.Reset();
            _position.Symbol = Signal.Symbol;
            _position.Direction = Signal.Direction;
            _position.TargetQty = Signal.Quantity;
            _pendingOrder = null;
        }
    }
}
