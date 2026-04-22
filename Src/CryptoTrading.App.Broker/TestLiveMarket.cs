using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.TradeRequest;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoTrading.App.Broker
{
    /// <summary>
    /// Paper-trading IMarket. Account queries hit the real exchange (so the
    /// algorithm sees a real balance), but order placement is simulated:
    /// we log the intent and return a synthetic <see cref="ExchangeOrder"/>
    /// in Filled state without calling any live trade endpoint.
    ///
    /// This replaces the earlier bundled-SDK implementation that called
    /// <c>_api.TestPlaceAsync</c> — Binance's /order/test endpoint only
    /// validates payloads, it doesn't return a useful order object, and
    /// routing through a synthetic fill here lets the rest of the algorithm
    /// observe a realistic order lifecycle during paper runs.
    /// </summary>
    public class TestLiveMarket : IMarket
    {
        private readonly IExchangeProvider _exchange;
        private readonly ILogger<TestLiveMarket> _logger;
        private static long _syntheticOrderIdSeed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public TestLiveMarket(ILogger<TestLiveMarket> logger, IExchangeProvider exchange)
        {
            _exchange = exchange;
            _logger = logger;
        }

        public Task<IEnumerable<ExchangeBalance>> GetAccountBalances()
        {
            // Read the real wallet so the algorithm's sizing decisions match
            // what it would see in live mode.
            return _exchange.GetBalancesAsync();
        }

        public Task<IEnumerable<ExchangeOrder>> GetAllOpenOrders()
        {
            // Paper mode never creates open orders — every synthetic fill is
            // immediately terminal. Return empty rather than hitting REST.
            return Task.FromResult<IEnumerable<ExchangeOrder>>(Array.Empty<ExchangeOrder>());
        }

        public Task<ExchangeOrder> SetMarketOrder(IMarketRequest trade)
        {
            return PaperFill(trade.Symbol, trade.OrderType, ExchangeOrderType.Market,
                price: trade.Price, quantity: trade.Quantity, stopPrice: 0m);
        }

        public Task<ExchangeOrder> SetLimitOrder(ILimitRequest trade)
        {
            return PaperFill(trade.Symbol, trade.OrderType, ExchangeOrderType.Limit,
                price: trade.Price, quantity: trade.Quantity, stopPrice: 0m);
        }

        public Task<ExchangeOrder> SetStopLimitOrder(IStopLimitRequest trade)
        {
            return PaperFill(trade.Symbol, trade.OrderType, ExchangeOrderType.StopLimit,
                price: trade.StopPrice, quantity: trade.Quantity, stopPrice: trade.StopPrice);
        }

        public Task<string> CancelOrder(ICancelRequest order)
        {
            _logger.LogInformation($"[paper] {order.Symbol} - {order.ClientOrderId} Cancelled");
            return Task.FromResult(order.ClientOrderId ?? string.Empty);
        }

        private Task<ExchangeOrder> PaperFill(
            string symbol, ExchangeOrderSide? orderType, ExchangeOrderType type,
            decimal price, decimal quantity, decimal stopPrice)
        {
            var id = Interlocked.Increment(ref _syntheticOrderIdSeed);
            var side = orderType ?? ExchangeOrderSide.Buy;
            var order = new ExchangeOrder
            {
                ExchangeId = _exchange.ExchangeId,
                OrderId = id.ToString(),
                ClientOrderId = $"paper-{id}",
                Symbol = symbol,
                Side = side,
                Type = type,
                Status = ExchangeOrderStatus.Filled,
                Price = price,
                StopPrice = stopPrice,
                Quantity = quantity,
                FilledQuantity = quantity,
                QuoteQuantity = price * quantity,
                Timestamp = DateTime.UtcNow
            };
            _logger.LogInformation(
                $"[paper] {symbol} {side} {type} qty={quantity} price={price} -> synthetic {order.Status}");
            return Task.FromResult(order);
        }
    }
}
