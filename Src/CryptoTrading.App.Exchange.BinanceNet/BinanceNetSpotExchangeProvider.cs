using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Binance.Net.Interfaces.Clients;
using CryptoTrading.App.Core.Exchange;
// Binance.Net.Enums.PositionSide collides with the neutral PositionSide
// we expose on IExchangeProvider. Alias only the enums we actually need
// from Binance.Net so the method signatures match the interface.
using BinanceTimeInForce = Binance.Net.Enums.TimeInForce;
using BinanceSpotOrderType = Binance.Net.Enums.SpotOrderType;

namespace CryptoTrading.App.Exchange.BinanceNet
{
    /// <summary>
    /// Binance.Net-backed IExchangeProvider for SPOT venues. Parallel to
    /// <c>BinanceExchangeProvider</c> (which wraps the bundled-SDK), and
    /// the first Binance.Net adapter to land — PR 3 adds USD-M futures,
    /// PR 4 adds margin. Spot-only guards match the bundled adapter so
    /// accidental futures/margin wiring fails loud rather than silently
    /// dropping the flag and placing a spot order.
    /// </summary>
    public class BinanceNetSpotExchangeProvider : IExchangeProvider
    {
        private readonly IBinanceRestClient _rest;
        private readonly IBinanceSocketClient _socket;

        public string ExchangeId => BinanceNetMapper.ExchangeName;

        public TradingVenue Venue => TradingVenue.Spot;

        public BinanceNetSpotExchangeProvider(IBinanceRestClient rest, IBinanceSocketClient socket)
        {
            _rest = rest ?? throw new ArgumentNullException(nameof(rest));
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        }

        #region Account

        public async Task<IEnumerable<ExchangeBalance>> GetBalancesAsync()
        {
            var result = await _rest.SpotApi.Account.GetAccountInfoAsync();
            EnsureSuccess(result, nameof(GetBalancesAsync));
            return result.Data.Balances
                .Where(b => b.Available > 0 || b.Locked > 0)
                .Select(BinanceNetMapper.ToExchangeBalance);
        }

        public async Task<IEnumerable<ExchangeSymbol>> GetSymbolsAsync()
        {
            var result = await _rest.SpotApi.ExchangeData.GetExchangeInfoAsync();
            EnsureSuccess(result, nameof(GetSymbolsAsync));
            return result.Data.Symbols.Select(BinanceNetMapper.ToExchangeSymbol);
        }

        public Task<ExchangeFeeSchedule> GetFeeScheduleAsync()
        {
            // Return the documented Binance standard fees (0.1% maker/taker,
            // 25% discount when paying in BNB). The per-account overridden
            // fees are accessible via SpotApi.Account.GetTradeFeeAsync but
            // require a symbol argument — consumers that care can query
            // directly. Matches the bundled adapter's behaviour.
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
            var result = await _rest.SpotApi.Trading.PlaceOrderAsync(
                symbol,
                BinanceNetMapper.MapToBinanceOrderSide(side),
                BinanceSpotOrderType.Market,
                quantity: quantity);
            EnsureSuccess(result, nameof(PlaceMarketOrderAsync));
            return BinanceNetMapper.ToExchangeOrder(result.Data);
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
            var result = await _rest.SpotApi.Trading.PlaceOrderAsync(
                symbol,
                BinanceNetMapper.MapToBinanceOrderSide(side),
                BinanceSpotOrderType.Limit,
                quantity: quantity,
                price: price,
                timeInForce: BinanceTimeInForce.GoodTillCanceled);
            EnsureSuccess(result, nameof(PlaceLimitOrderAsync));
            return BinanceNetMapper.ToExchangeOrder(result.Data);
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
            var result = await _rest.SpotApi.Trading.PlaceOrderAsync(
                symbol,
                BinanceNetMapper.MapToBinanceOrderSide(side),
                BinanceSpotOrderType.StopLossLimit,
                quantity: quantity,
                price: limitPrice,
                stopPrice: stopPrice,
                timeInForce: BinanceTimeInForce.GoodTillCanceled);
            EnsureSuccess(result, nameof(PlaceStopLimitOrderAsync));
            return BinanceNetMapper.ToExchangeOrder(result.Data);
        }

        /// <summary>
        /// Spot venue cannot consume futures/margin-only flags. Fail loud
        /// if a caller routes futures/margin intent through the spot
        /// provider — the sizing and fill assumptions would silently be
        /// wrong.
        /// </summary>
        private static void GuardSpotOnlyParams(
            PositionSide positionSide, MarginSideEffect marginSideEffect, bool reduceOnly)
        {
            if (positionSide != PositionSide.Both)
                throw new NotSupportedException(
                    $"{nameof(BinanceNetSpotExchangeProvider)} is spot-only; positionSide={positionSide} is not supported. " +
                    "Use the futures provider (PR 3+).");
            if (marginSideEffect != MarginSideEffect.None)
                throw new NotSupportedException(
                    $"{nameof(BinanceNetSpotExchangeProvider)} is spot-only; marginSideEffect={marginSideEffect} is not supported. " +
                    "Use the margin provider (PR 4+).");
            if (reduceOnly)
                throw new NotSupportedException(
                    $"{nameof(BinanceNetSpotExchangeProvider)} is spot-only; reduceOnly=true is not supported. " +
                    "Use the futures provider (PR 3+).");
        }

        public async Task<ExchangeOrder> GetOrderAsync(string symbol, string orderId)
        {
            if (long.TryParse(orderId, out var id))
            {
                var result = await _rest.SpotApi.Trading.GetOrderAsync(symbol, orderId: id);
                EnsureSuccess(result, nameof(GetOrderAsync));
                return BinanceNetMapper.ToExchangeOrder(result.Data);
            }

            // Not an exchange-assigned numeric id — treat as client order id.
            var byClient = await _rest.SpotApi.Trading.GetOrderAsync(symbol, origClientOrderId: orderId);
            EnsureSuccess(byClient, nameof(GetOrderAsync));
            return BinanceNetMapper.ToExchangeOrder(byClient.Data);
        }

        public async Task<ExchangeOrder> CancelOrderAsync(string symbol, string orderId)
        {
            if (long.TryParse(orderId, out var id))
            {
                var cancel = await _rest.SpotApi.Trading.CancelOrderAsync(symbol, orderId: id);
                EnsureSuccess(cancel, nameof(CancelOrderAsync));
            }
            else
            {
                var cancel = await _rest.SpotApi.Trading.CancelOrderAsync(symbol, origClientOrderId: orderId);
                EnsureSuccess(cancel, nameof(CancelOrderAsync));
            }

            // Re-query for the post-cancel state so consumers see the
            // Cancelled status rather than whatever the cancel response
            // happens to ship back.
            return await GetOrderAsync(symbol, orderId);
        }

        public async Task<IEnumerable<ExchangeOrder>> GetOpenOrdersAsync()
        {
            var result = await _rest.SpotApi.Trading.GetOpenOrdersAsync();
            EnsureSuccess(result, nameof(GetOpenOrdersAsync));
            return result.Data.Select(BinanceNetMapper.ToExchangeOrder);
        }

        #endregion

        #region Market Data (REST)

        public async Task<IEnumerable<ExchangeCandlestick>> GetCandlesticksAsync(
            string symbol, CandleInterval interval, DateTime from, DateTime to)
        {
            var binanceInterval = BinanceNetMapper.MapToBinanceKlineInterval(interval);
            var result = await _rest.SpotApi.ExchangeData.GetKlinesAsync(symbol, binanceInterval, from, to);
            EnsureSuccess(result, nameof(GetCandlesticksAsync));
            return result.Data.Select(k => BinanceNetMapper.ToExchangeCandlestick(k, symbol, interval));
        }

        #endregion

        #region Market Data (WebSocket)

        public async Task SubscribeCandlestickAsync(
            string symbol, CandleInterval interval, Action<ExchangeCandlestick> onCandle)
        {
            if (onCandle == null) throw new ArgumentNullException(nameof(onCandle));

            var binanceInterval = BinanceNetMapper.MapToBinanceKlineInterval(interval);
            var result = await _socket.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(
                symbol,
                binanceInterval,
                evt =>
                {
                    // DataEvent<IBinanceStreamKlineData>; the inner Data
                    // carries the IBinanceStreamKline we actually care about
                    // (OHLC + Interval + Final flag in one type).
                    var kline = evt.Data?.Data;
                    if (kline == null) return;
                    onCandle(BinanceNetMapper.ToExchangeCandlestick(kline, symbol));
                });
            EnsureSuccess(result, nameof(SubscribeCandlestickAsync));
        }

        public async Task UnsubscribeAllAsync()
        {
            await _socket.UnsubscribeAllAsync();
        }

        #endregion

        #region Positions & Leverage (spot = no-op)

        public Task<IEnumerable<ExchangePosition>> GetPositionsAsync()
        {
            // Spot does not have signed positions; net exposure lives in
            // ExchangeBalance. Futures/margin providers override.
            return Task.FromResult<IEnumerable<ExchangePosition>>(Array.Empty<ExchangePosition>());
        }

        public Task SetLeverageAsync(string symbol, int leverage)
        {
            if (leverage != 1)
                throw new NotSupportedException(
                    $"{nameof(BinanceNetSpotExchangeProvider)} is spot-only; leverage={leverage} is not supported. " +
                    "Use the futures provider (PR 3+).");
            return Task.CompletedTask;
        }

        #endregion

        #region User Data Stream

        public async Task SubscribeUserStreamAsync(Action<ExchangeFill> onFill)
        {
            if (onFill == null) throw new ArgumentNullException(nameof(onFill));

            // Binance.Net 12.x auto-manages the spot listen-key lifecycle
            // inside SubscribeToUserDataUpdatesAsync, so we only need to
            // hand it the order-update handler. Other callbacks (OCO,
            // balance update, positions) are left null — the trade
            // monitor reconciles those through REST snapshots on refresh.
            var result = await _socket.SpotApi.Account.SubscribeToUserDataUpdatesAsync(
                onOrderUpdateMessage: evt =>
                {
                    if (evt.Data == null) return;
                    onFill(BinanceNetMapper.ToExchangeFill(evt.Data));
                },
                onOcoOrderUpdateMessage: null,
                onAccountPositionMessage: null,
                onAccountBalanceUpdate: null,
                onUserDataStreamTerminated: null,
                onBalanceLockUpdate: null);
            EnsureSuccess(result, nameof(SubscribeUserStreamAsync));
        }

        #endregion

        /// <summary>
        /// Binance.Net wraps every call in a WebCallResult; non-Success
        /// responses carry the error code/message. Translate them to a
        /// single InvalidOperationException so the caller doesn't have
        /// to reach into CryptoExchange.Net types.
        /// </summary>
        private static void EnsureSuccess<T>(CryptoExchange.Net.Objects.WebCallResult<T> result, string operation)
        {
            if (result == null)
                throw new InvalidOperationException($"Binance.Net returned null for {operation}.");
            if (!result.Success)
                throw new InvalidOperationException(
                    $"Binance.Net {operation} failed: {result.Error?.Code} {result.Error?.Message}");
        }

        private static void EnsureSuccess(CryptoExchange.Net.Objects.CallResult result, string operation)
        {
            if (result == null)
                throw new InvalidOperationException($"Binance.Net returned null for {operation}.");
            if (!result.Success)
                throw new InvalidOperationException(
                    $"Binance.Net {operation} failed: {result.Error?.Code} {result.Error?.Message}");
        }
    }
}
