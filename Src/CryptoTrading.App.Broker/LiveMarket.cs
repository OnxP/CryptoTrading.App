using Binance;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.TradeRequest;
using CryptoTrading.App.Exchange.BinanceAdapter;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.Broker
{
    /// <summary>
    /// Bundled-SDK-backed IMarket. Internally calls the Binance SDK, then
    /// adapts every return value to the neutral <see cref="ExchangeOrder"/> /
    /// <see cref="ExchangeBalance"/> types so downstream code never touches
    /// Binance.* directly.
    /// </summary>
    public class LiveMarket : IMarket
    {
        private readonly IBinanceApi _api;
        private readonly IBinanceApiUser _user;
        private readonly ILogger<LiveMarket> _logger;
        public LiveMarket(ILogger<LiveMarket> logger,IBinanceApi api,IBinanceApiUser user)
        {
            _api = api;
            _user = user;
            _logger = logger;
        }
        public async Task<IEnumerable<ExchangeBalance>> GetAccountBalances()
        {
            var accountBalances = await _api.GetAccountInfoAsync(_user);
            return accountBalances.Balances
                .Where(x => x.Free > 0)
                .Select(BinanceMapper.ToExchangeBalance);
        }

        public Task<IEnumerable<ExchangeOrder>> GetAllOpenOrders()
        {
            throw new NotImplementedException();
        }

        public async Task<ExchangeOrder> SetMarketOrder(IMarketRequest trade)
        {
            var clientOrder = new MarketOrder(_user)
            {
                Symbol = trade.Symbol,
                Side = BinanceMapper.MapToBinanceOrderSide(trade.OrderType ?? ExchangeOrderSide.Buy),
                Quantity = trade.Quantity
            };

            var order = await _api.PlaceAsync(clientOrder);
            return BinanceMapper.ToExchangeOrder(order);
        }

        public async Task<ExchangeOrder> SetStopLimitOrder(IStopLimitRequest trade)
        {
            var clientOrder = new LimitOrder(_user)
            {
                Symbol = trade.Symbol,
                Side = BinanceMapper.MapToBinanceOrderSide(trade.OrderType ?? ExchangeOrderSide.Buy),
                Price = trade.StopPrice,
                Quantity = trade.Quantity
            };

            var order = await _api.PlaceAsync(clientOrder);
            LogOrder(order.Symbol, order.ClientOrderId, OrderStatus.Filled);

            return BinanceMapper.ToExchangeOrder(order);
        }

        private void LogOrder(string symbol, string order, OrderStatus filled)
        {
            _logger.LogInformation($"{symbol} - {order} {filled.ToString()}");
        }

        public async Task<string> CancelOrder(ICancelRequest order)
        {
            _logger.LogInformation($"{order.Symbol} - {order.ClientOrderId} Cancelled");
            if (long.TryParse(order.ClientOrderId, out var numericId))
            {
                return await _api.CancelOrderAsync(_user, order.Symbol, numericId);
            }
            return await _api.CancelOrderAsync(_user, order.Symbol, order.ClientOrderId);
        }

        public async Task<ExchangeOrder> SetLimitOrder(ILimitRequest trade)
        {
            var clientOrder = new LimitOrder(_user)
            {
                Symbol = trade.Symbol,
                Side = BinanceMapper.MapToBinanceOrderSide(trade.OrderType ?? ExchangeOrderSide.Buy),
                Quantity = trade.Quantity
            };

            var order = await _api.PlaceAsync(clientOrder);
            return BinanceMapper.ToExchangeOrder(order);
        }
    }
}
