using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Tests.Mocks
{
    /// <summary>
    /// In-memory implementation of IExchangeProvider for testing.
    /// Configurable balances, symbols, and fee schedule.
    /// Records all order calls for assertion.
    /// </summary>
    public class MockExchangeProvider : IExchangeProvider
    {
        public string ExchangeId { get; }

        // Configurable state
        public List<ExchangeBalance> Balances { get; set; } = new List<ExchangeBalance>();
        public List<ExchangeSymbol> Symbols { get; set; } = new List<ExchangeSymbol>();
        public ExchangeFeeSchedule FeeSchedule { get; set; }
        public List<ExchangeOrder> OpenOrders { get; set; } = new List<ExchangeOrder>();

        // Recorded actions for assertion
        public List<ExchangeOrder> PlacedOrders { get; } = new List<ExchangeOrder>();
        public List<string> CancelledOrderIds { get; } = new List<string>();
        public List<(string Symbol, CandleInterval Interval)> CandlestickSubscriptions { get; } = new List<(string, CandleInterval)>();

        // Configurable candlestick data
        public Dictionary<string, List<ExchangeCandlestick>> CandlestickData { get; set; } = new Dictionary<string, List<ExchangeCandlestick>>();

        // WebSocket simulation
        private readonly Dictionary<string, Action<ExchangeCandlestick>> _candlestickCallbacks = new Dictionary<string, Action<ExchangeCandlestick>>();

        public MockExchangeProvider(string exchangeId)
        {
            ExchangeId = exchangeId;
            FeeSchedule = new ExchangeFeeSchedule(exchangeId, 0.001m, 0.001m, "USDT");
        }

        public Task<IEnumerable<ExchangeBalance>> GetBalancesAsync()
            => Task.FromResult<IEnumerable<ExchangeBalance>>(Balances);

        public Task<IEnumerable<ExchangeSymbol>> GetSymbolsAsync()
            => Task.FromResult<IEnumerable<ExchangeSymbol>>(Symbols);

        public Task<ExchangeFeeSchedule> GetFeeScheduleAsync()
            => Task.FromResult(FeeSchedule);

        public Task<ExchangeOrder> PlaceMarketOrderAsync(string symbol, ExchangeOrderSide side, decimal quantity)
        {
            var order = ExchangeOrder.CreateFilledOrder(ExchangeId, symbol, side, 50000m, quantity, DateTime.UtcNow);
            PlacedOrders.Add(order);
            return Task.FromResult(order);
        }

        public Task<ExchangeOrder> PlaceLimitOrderAsync(string symbol, ExchangeOrderSide side, decimal price, decimal quantity)
        {
            var order = new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = Guid.NewGuid().ToString(),
                Symbol = symbol,
                Side = side,
                Type = ExchangeOrderType.Limit,
                Status = ExchangeOrderStatus.New,
                Price = price,
                Quantity = quantity,
                Timestamp = DateTime.UtcNow
            };
            PlacedOrders.Add(order);
            OpenOrders.Add(order);
            return Task.FromResult(order);
        }

        public Task<ExchangeOrder> PlaceStopLimitOrderAsync(string symbol, ExchangeOrderSide side, decimal stopPrice, decimal limitPrice, decimal quantity)
        {
            var order = new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = Guid.NewGuid().ToString(),
                Symbol = symbol,
                Side = side,
                Type = ExchangeOrderType.StopLimit,
                Status = ExchangeOrderStatus.New,
                Price = limitPrice,
                StopPrice = stopPrice,
                Quantity = quantity,
                Timestamp = DateTime.UtcNow
            };
            PlacedOrders.Add(order);
            OpenOrders.Add(order);
            return Task.FromResult(order);
        }

        public Task<ExchangeOrder> GetOrderAsync(string symbol, string orderId)
        {
            var order = PlacedOrders.FirstOrDefault(o => o.OrderId == orderId && o.Symbol == symbol);
            return Task.FromResult(order);
        }

        public Task<ExchangeOrder> CancelOrderAsync(string symbol, string orderId)
        {
            CancelledOrderIds.Add(orderId);
            var order = OpenOrders.FirstOrDefault(o => o.OrderId == orderId);
            if (order != null)
            {
                order.Status = ExchangeOrderStatus.Cancelled;
                OpenOrders.Remove(order);
            }
            return Task.FromResult(order);
        }

        public Task<IEnumerable<ExchangeOrder>> GetOpenOrdersAsync()
            => Task.FromResult<IEnumerable<ExchangeOrder>>(OpenOrders);

        public Task<IEnumerable<ExchangeCandlestick>> GetCandlesticksAsync(string symbol, CandleInterval interval, DateTime from, DateTime to)
        {
            var key = $"{symbol}_{interval}";
            if (CandlestickData.TryGetValue(key, out var candles))
            {
                var filtered = candles.Where(c => c.OpenTime >= from && c.OpenTime <= to);
                return Task.FromResult(filtered);
            }
            return Task.FromResult(Enumerable.Empty<ExchangeCandlestick>());
        }

        public Task SubscribeCandlestickAsync(string symbol, CandleInterval interval, Action<ExchangeCandlestick> onCandle)
        {
            var key = $"{symbol}_{interval}";
            _candlestickCallbacks[key] = onCandle;
            CandlestickSubscriptions.Add((symbol, interval));
            return Task.CompletedTask;
        }

        public Task UnsubscribeAllAsync()
        {
            _candlestickCallbacks.Clear();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Simulate receiving a candlestick (for testing WebSocket behavior)
        /// </summary>
        public void SimulateCandlestick(ExchangeCandlestick candle)
        {
            var key = $"{candle.Symbol}_{candle.Interval}";
            if (_candlestickCallbacks.TryGetValue(key, out var callback))
            {
                callback(candle);
            }
        }

        /// <summary>
        /// Simulate filling an open order
        /// </summary>
        public void SimulateFill(string orderId, decimal fillPrice)
        {
            var order = OpenOrders.FirstOrDefault(o => o.OrderId == orderId);
            if (order != null)
            {
                order.Status = ExchangeOrderStatus.Filled;
                order.Price = fillPrice;
                order.FilledQuantity = order.Quantity;
                order.QuoteQuantity = fillPrice * order.Quantity;
                OpenOrders.Remove(order);
            }
        }
    }
}
