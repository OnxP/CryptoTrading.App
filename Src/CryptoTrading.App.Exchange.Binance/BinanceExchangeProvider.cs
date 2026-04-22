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

        /// <summary>
        /// Bundled-SDK provider only supports spot. Margin and futures routes
        /// go through the Binance.Net-backed provider (introduced in PR 2+).
        /// </summary>
        public TradingVenue Venue => TradingVenue.Spot;

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

        public async Task<ExchangeOrder> PlaceMarketOrderAsync(
            string symbol,
            ExchangeOrderSide side,
            decimal quantity,
            PositionSide positionSide = PositionSide.Both,
            MarginSideEffect marginSideEffect = MarginSideEffect.None,
            bool reduceOnly = false)
        {
            GuardSpotOnlyParams(positionSide, marginSideEffect, reduceOnly);
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

        public async Task<ExchangeOrder> PlaceLimitOrderAsync(
            string symbol,
            ExchangeOrderSide side,
            decimal price,
            decimal quantity,
            PositionSide positionSide = PositionSide.Both,
            MarginSideEffect marginSideEffect = MarginSideEffect.None,
            bool reduceOnly = false)
        {
            GuardSpotOnlyParams(positionSide, marginSideEffect, reduceOnly);
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

        public async Task<ExchangeOrder> PlaceStopLimitOrderAsync(
            string symbol,
            ExchangeOrderSide side,
            decimal stopPrice,
            decimal limitPrice,
            decimal quantity,
            PositionSide positionSide = PositionSide.Both,
            MarginSideEffect marginSideEffect = MarginSideEffect.None,
            bool reduceOnly = false)
        {
            GuardSpotOnlyParams(positionSide, marginSideEffect, reduceOnly);
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

        /// <summary>
        /// The bundled-SDK provider is spot-only. Any caller passing
        /// non-default margin/futures parameters is wiring this adapter
        /// into the wrong venue — fail loud rather than silently drop the
        /// flag and place a spot order.
        /// </summary>
        private static void GuardSpotOnlyParams(
            PositionSide positionSide, MarginSideEffect marginSideEffect, bool reduceOnly)
        {
            if (positionSide != PositionSide.Both)
                throw new NotSupportedException(
                    $"{nameof(BinanceExchangeProvider)} is spot-only; positionSide={positionSide} is not supported. " +
                    "Use the futures provider (introduced in PR 3).");
            if (marginSideEffect != MarginSideEffect.None)
                throw new NotSupportedException(
                    $"{nameof(BinanceExchangeProvider)} is spot-only; marginSideEffect={marginSideEffect} is not supported. " +
                    "Use the margin provider (introduced in PR 4).");
            if (reduceOnly)
                throw new NotSupportedException(
                    $"{nameof(BinanceExchangeProvider)} is spot-only; reduceOnly=true is not supported. " +
                    "Use the futures provider (introduced in PR 3).");
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

        #region Positions & Leverage (spot-only = always empty / no-op)

        public Task<IEnumerable<ExchangePosition>> GetPositionsAsync()
        {
            // Spot has no position concept — net exposure is derivable from
            // balances. Consumers should not rely on this for spot venues.
            return Task.FromResult<IEnumerable<ExchangePosition>>(Array.Empty<ExchangePosition>());
        }

        public Task SetLeverageAsync(string symbol, int leverage)
        {
            if (leverage != 1)
                throw new NotSupportedException(
                    $"{nameof(BinanceExchangeProvider)} is spot-only; leverage={leverage} is not supported. " +
                    "Use the futures provider (introduced in PR 3).");
            return Task.CompletedTask;
        }

        #endregion

        #region User Data Stream

        public Task SubscribeUserStreamAsync(Action<ExchangeFill> onFill)
        {
            // User-stream support for the bundled SDK requires wiring
            // IUserDataWebSocketClient. Deferred to PR 2 where the Binance.Net
            // provider owns this responsibility across all venues.
            throw new NotImplementedException(
                "User-stream fills are wired through the Binance.Net provider in PR 2+. " +
                "Order state for the bundled SDK currently polls through TradeMonitor.CheckOrder.");
        }

        #endregion
    }
}
