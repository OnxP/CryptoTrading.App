using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Binance;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Exchange.BinanceAdapter
{
    /// <summary>
    /// Binance implementation of IExchangeProvider.
    /// Wraps the existing Binance SDK behind the exchange-agnostic interface.
    /// </summary>
    public class BinanceExchangeProvider : IExchangeProvider
    {
        private readonly IBinanceApi _api;
        private readonly IBinanceApiUser _user;

        public string ExchangeId => BinanceMapper.ExchangeName;

        public BinanceExchangeProvider(IBinanceApi api, IBinanceApiUser user)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        #region Account

        public async Task<IEnumerable<ExchangeBalance>> GetBalancesAsync()
        {
            var accountInfo = await _api.GetAccountInfoAsync(_user);
            return accountInfo.Balances
                .Where(b => b.Free > 0 || b.Locked > 0)
                .Select(BinanceMapper.ToExchangeBalance);
        }

        public async Task<IEnumerable<ExchangeSymbol>> GetSymbolsAsync()
        {
            var symbols = await _api.GetSymbolsAsync();
            return symbols.Select(BinanceMapper.ToExchangeSymbol);
        }

        public Task<ExchangeFeeSchedule> GetFeeScheduleAsync()
        {
            // Binance standard fees: 0.1% maker/taker
            // BNB discount: 25% off when paying fees with BNB
            var schedule = new ExchangeFeeSchedule(ExchangeId, 0.001m, 0.001m, "BNB")
            {
                HasFeeDiscount = true,
                DiscountRate = 0.25m
            };
            return Task.FromResult(schedule);
        }

        #endregion

        #region Orders

        public async Task<ExchangeOrder> PlaceMarketOrderAsync(string symbol, ExchangeOrderSide side, decimal quantity)
        {
            var binanceSide = BinanceMapper.MapToBinanceOrderSide(side);
            var clientOrder = new MarketOrder(_user)
            {
                Symbol = symbol,
                Side = binanceSide,
                Quantity = quantity
            };

            var order = await _api.PlaceAsync(clientOrder);
            return BinanceMapper.ToExchangeOrder(order);
        }

        public async Task<ExchangeOrder> PlaceLimitOrderAsync(string symbol, ExchangeOrderSide side, decimal price, decimal quantity)
        {
            var binanceSide = BinanceMapper.MapToBinanceOrderSide(side);
            var clientOrder = new LimitOrder(_user)
            {
                Symbol = symbol,
                Side = binanceSide,
                Price = price,
                Quantity = quantity
            };

            var order = await _api.PlaceAsync(clientOrder);
            return BinanceMapper.ToExchangeOrder(order);
        }

        public async Task<ExchangeOrder> PlaceStopLimitOrderAsync(string symbol, ExchangeOrderSide side, decimal stopPrice, decimal limitPrice, decimal quantity)
        {
            var binanceSide = BinanceMapper.MapToBinanceOrderSide(side);
            var clientOrder = new StopLossLimitOrder(_user)
            {
                Symbol = symbol,
                Side = binanceSide,
                StopPrice = stopPrice,
                Price = limitPrice,
                Quantity = quantity
            };

            var order = await _api.PlaceAsync(clientOrder);
            return BinanceMapper.ToExchangeOrder(order);
        }

        public async Task<ExchangeOrder> GetOrderAsync(string symbol, string orderId)
        {
            if (long.TryParse(orderId, out var id))
            {
                var order = await _api.GetOrderAsync(_user, symbol, id);
                return BinanceMapper.ToExchangeOrder(order);
            }

            // Try as client order ID
            var orderByClient = await _api.GetOrderAsync(_user, symbol, orderId);
            return BinanceMapper.ToExchangeOrder(orderByClient);
        }

        public async Task<ExchangeOrder> CancelOrderAsync(string symbol, string orderId)
        {
            if (long.TryParse(orderId, out var id))
            {
                await _api.CancelOrderAsync(_user, symbol, id);
            }
            else
            {
                await _api.CancelOrderAsync(_user, symbol, orderId);
            }

            // Return the cancelled order state
            return await GetOrderAsync(symbol, orderId);
        }

        public async Task<IEnumerable<ExchangeOrder>> GetOpenOrdersAsync()
        {
            var orders = await _api.GetOpenOrdersAsync(_user);
            return orders.Select(BinanceMapper.ToExchangeOrder);
        }

        #endregion

        #region Market Data

        public async Task<IEnumerable<ExchangeCandlestick>> GetCandlesticksAsync(string symbol, CandleInterval interval, DateTime from, DateTime to)
        {
            var binanceInterval = BinanceMapper.MapToBinanceCandleInterval(interval);
            var candles = await _api.GetCandlesticksAsync(symbol, binanceInterval, startTime: from, endTime: to);
            return candles.Select(BinanceMapper.ToExchangeCandlestick);
        }

        public Task SubscribeCandlestickAsync(string symbol, CandleInterval interval, Action<ExchangeCandlestick> onCandle)
        {
            // WebSocket subscription will be wired through the existing
            // CandlestickWebSocketClient infrastructure.
            // This is a placeholder - the actual implementation will use
            // ICandlestickWebSocketClient from the Binance SDK.
            throw new NotImplementedException(
                "WebSocket candlestick streaming requires ICandlestickWebSocketClient integration. " +
                "Use the existing LiveMarketData infrastructure for now.");
        }

        public Task UnsubscribeAllAsync()
        {
            // Will be implemented with WebSocket client cleanup
            return Task.CompletedTask;
        }

        #endregion
    }
}
