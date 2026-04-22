using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.TradeRequest;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoTrading.App.Broker
{
    /// <summary>
    /// IExchangeProvider-backed IMarket. Routes order placement and account
    /// queries through the venue-aware <see cref="IExchangeProvider"/> so the
    /// Broker subsystem is exchange- and SDK-agnostic: swapping between the
    /// Binance.Net spot / margin / futures providers is a single DI switch.
    ///
    /// Replaces the earlier bundled-SDK implementation that depended on
    /// <c>Binance.IBinanceApi</c> + <c>IBinanceApiUser</c>. All margin /
    /// futures flags (<see cref="MarginSideEffect"/>, <see cref="PositionSide"/>,
    /// reduceOnly) are left at their neutral defaults here — the current
    /// IMarket contract doesn't plumb them through, so callers that need
    /// them must reach for <see cref="IExchangeProvider"/> directly until
    /// <c>IMarket</c> grows venue-aware fields in a follow-up PR.
    /// </summary>
    public class LiveMarket : IMarket
    {
        private readonly IExchangeProvider _exchange;
        private readonly ILogger<LiveMarket> _logger;

        public LiveMarket(ILogger<LiveMarket> logger, IExchangeProvider exchange)
        {
            _exchange = exchange;
            _logger = logger;
        }

        public Task<IEnumerable<ExchangeBalance>> GetAccountBalances()
        {
            return _exchange.GetBalancesAsync();
        }

        public Task<IEnumerable<ExchangeOrder>> GetAllOpenOrders()
        {
            return _exchange.GetOpenOrdersAsync();
        }

        public async Task<ExchangeOrder> SetMarketOrder(IMarketRequest trade)
        {
            var side = trade.OrderType ?? ExchangeOrderSide.Buy;
            var order = await _exchange.PlaceMarketOrderAsync(trade.Symbol, side, trade.Quantity);
            LogOrder(order);
            return order;
        }

        public async Task<ExchangeOrder> SetLimitOrder(ILimitRequest trade)
        {
            var side = trade.OrderType ?? ExchangeOrderSide.Buy;
            var order = await _exchange.PlaceLimitOrderAsync(trade.Symbol, side, trade.Price, trade.Quantity);
            LogOrder(order);
            return order;
        }

        public async Task<ExchangeOrder> SetStopLimitOrder(IStopLimitRequest trade)
        {
            // The old bundled-SDK path treated IStopLimitRequest.StopPrice as
            // BOTH the trigger and the limit price (see legacy LiveMarket).
            // Preserve that contract so calling algorithms don't change
            // behaviour — callers that need a distinct limit price should
            // move to IExchangeProvider.PlaceStopLimitOrderAsync directly.
            var side = trade.OrderType ?? ExchangeOrderSide.Buy;
            var order = await _exchange.PlaceStopLimitOrderAsync(
                trade.Symbol, side, trade.StopPrice, trade.StopPrice, trade.Quantity);
            LogOrder(order);
            return order;
        }

        public async Task<string> CancelOrder(ICancelRequest order)
        {
            _logger.LogInformation($"{order.Symbol} - {order.ClientOrderId} Cancelled");
            var cancelled = await _exchange.CancelOrderAsync(order.Symbol, order.ClientOrderId);
            return cancelled.OrderId;
        }

        private void LogOrder(ExchangeOrder order)
        {
            _logger.LogInformation($"{order.Symbol} - {order.OrderId} {order.Status}");
        }
    }
}
