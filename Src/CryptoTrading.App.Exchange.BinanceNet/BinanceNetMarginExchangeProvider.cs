using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Binance.Net.Interfaces.Clients;
using CryptoTrading.App.Core.Exchange;
// Alias the Binance enums we actually touch — the neutral versions share
// names with Binance.Net.Enums types (OrderSide, TimeInForce, SpotOrderType).
using BinanceTimeInForce = Binance.Net.Enums.TimeInForce;
using BinanceSpotOrderType = Binance.Net.Enums.SpotOrderType;

namespace CryptoTrading.App.Exchange.BinanceNet
{
    /// <summary>
    /// Binance.Net-backed IExchangeProvider for the Binance CROSS-MARGIN
    /// venue. Margin trades sit on the spot order book but the account is
    /// separately collateralised and positions are funded by borrowed assets,
    /// so all endpoints live under <c>_rest.SpotApi</c> but on the
    /// <c>Margin*</c> methods rather than the plain spot ones.
    ///
    /// Surface notes:
    /// <list type="bullet">
    /// <item>Balances come from the margin account snapshot (not the spot
    /// account), so locked/free quantities reflect borrowed + owned.</item>
    /// <item>Placement routes through <see cref="MarginSideEffect"/> — the
    /// algorithm passes <c>AutoBorrow</c> on entry and <c>AutoRepay</c> on
    /// exit so we never need to call the explicit borrow/repay endpoints.
    /// Explicit borrow/repay methods are still exposed on the REST client
    /// and can be wired through a follow-up if a consumer needs them.</item>
    /// <item><see cref="SetLeverageAsync"/> is a no-op: cross-margin leverage
    /// is not a per-symbol setting — it's implicit in how much the account
    /// chooses to borrow per order.</item>
    /// <item>User-stream fills require an explicit margin listen-token
    /// (<c>GetMarginUserListenTokenAsync</c>) before subscribing — unlike
    /// spot, which auto-manages the key. Missing that call is the single
    /// most common reason a margin user stream stays silent.</item>
    /// </list>
    /// Isolated margin is intentionally NOT wired here; it needs a per-symbol
    /// <c>isIsolated = true</c> flag on every call and the neutral interface
    /// has no venue for that yet. PR 5 adds an isolated-margin flag if we
    /// decide we need it.
    /// </summary>
    public class BinanceNetMarginExchangeProvider : IExchangeProvider
    {
        private readonly IBinanceRestClient _rest;
        private readonly IBinanceSocketClient _socket;

        public string ExchangeId => BinanceNetMapper.ExchangeName;

        public TradingVenue Venue => TradingVenue.Margin;

        public BinanceNetMarginExchangeProvider(IBinanceRestClient rest, IBinanceSocketClient socket)
        {
            _rest = rest ?? throw new ArgumentNullException(nameof(rest));
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        }

        #region Account

        public async Task<IEnumerable<ExchangeBalance>> GetBalancesAsync()
        {
            var result = await _rest.SpotApi.Account.GetMarginAccountInfoAsync();
            EnsureSuccess(result, nameof(GetBalancesAsync));
            return result.Data.Balances
                .Where(b => b.Available > 0 || b.Locked > 0 || b.Borrowed > 0 || b.Interest > 0)
                .Select(BinanceNetMapper.ToExchangeBalance);
        }

        public async Task<IEnumerable<ExchangeSymbol>> GetSymbolsAsync()
        {
            // Margin pairs are a subset of spot pairs with identical lot/price
            // filters (same order book, same matching engine). Reuse the spot
            // exchange-info feed so downstream sizing logic doesn't need a
            // separate code path. A follow-up could filter by GetMarginSymbols
            // if the consumer really needs the margin-only subset.
            var result = await _rest.SpotApi.ExchangeData.GetExchangeInfoAsync();
            EnsureSuccess(result, nameof(GetSymbolsAsync));
            return result.Data.Symbols.Select(BinanceNetMapper.ToExchangeSymbol);
        }

        public Task<ExchangeFeeSchedule> GetFeeScheduleAsync()
        {
            // Margin trades hit the spot order book, so maker/taker match the
            // VIP-0 spot tier (0.1%). BNB discount still applies.
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
            GuardMarginOnlyParams(positionSide, reduceOnly);
            var sideEffect = BinanceNetMapper.MapToBinanceSideEffectType(marginSideEffect);
            var result = await _rest.SpotApi.Trading.PlaceMarginOrderAsync(
                symbol,
                BinanceNetMapper.MapToBinanceOrderSide(side),
                BinanceSpotOrderType.Market,
                quantity: quantity,
                sideEffectType: sideEffect);
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
            GuardMarginOnlyParams(positionSide, reduceOnly);
            var sideEffect = BinanceNetMapper.MapToBinanceSideEffectType(marginSideEffect);
            var result = await _rest.SpotApi.Trading.PlaceMarginOrderAsync(
                symbol,
                BinanceNetMapper.MapToBinanceOrderSide(side),
                BinanceSpotOrderType.Limit,
                quantity: quantity,
                price: price,
                timeInForce: BinanceTimeInForce.GoodTillCanceled,
                sideEffectType: sideEffect);
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
            GuardMarginOnlyParams(positionSide, reduceOnly);
            var sideEffect = BinanceNetMapper.MapToBinanceSideEffectType(marginSideEffect);
            var result = await _rest.SpotApi.Trading.PlaceMarginOrderAsync(
                symbol,
                BinanceNetMapper.MapToBinanceOrderSide(side),
                BinanceSpotOrderType.StopLossLimit,
                quantity: quantity,
                price: limitPrice,
                stopPrice: stopPrice,
                timeInForce: BinanceTimeInForce.GoodTillCanceled,
                sideEffectType: sideEffect);
            EnsureSuccess(result, nameof(PlaceStopLimitOrderAsync));
            return BinanceNetMapper.ToExchangeOrder(result.Data);
        }

        /// <summary>
        /// Reject futures-only flags on margin orders. Margin uses the spot
        /// order model (no signed position side, no reduceOnly) — if we
        /// silently dropped these the algorithm's sizing assumption would
        /// be wrong in ways that only show up at unwind time.
        /// </summary>
        private static void GuardMarginOnlyParams(PositionSide positionSide, bool reduceOnly)
        {
            if (positionSide != PositionSide.Both)
                throw new NotSupportedException(
                    $"{nameof(BinanceNetMarginExchangeProvider)} does not support positionSide={positionSide}; " +
                    "margin uses the spot order model. Use the futures provider for dual-position mode.");
            if (reduceOnly)
                throw new NotSupportedException(
                    $"{nameof(BinanceNetMarginExchangeProvider)} does not support reduceOnly; " +
                    "margin has no signed position to reduce. Use the futures provider.");
        }

        public async Task<ExchangeOrder> GetOrderAsync(string symbol, string orderId)
        {
            if (long.TryParse(orderId, out var id))
            {
                var result = await _rest.SpotApi.Trading.GetMarginOrderAsync(symbol, orderId: id);
                EnsureSuccess(result, nameof(GetOrderAsync));
                return BinanceNetMapper.ToExchangeOrder(result.Data);
            }

            var byClient = await _rest.SpotApi.Trading.GetMarginOrderAsync(symbol, origClientOrderId: orderId);
            EnsureSuccess(byClient, nameof(GetOrderAsync));
            return BinanceNetMapper.ToExchangeOrder(byClient.Data);
        }

        public async Task<ExchangeOrder> CancelOrderAsync(string symbol, string orderId)
        {
            if (long.TryParse(orderId, out var id))
            {
                var cancel = await _rest.SpotApi.Trading.CancelMarginOrderAsync(symbol, orderId: id);
                EnsureSuccess(cancel, nameof(CancelOrderAsync));
            }
            else
            {
                var cancel = await _rest.SpotApi.Trading.CancelMarginOrderAsync(symbol, origClientOrderId: orderId);
                EnsureSuccess(cancel, nameof(CancelOrderAsync));
            }

            // Re-query so consumers observe the Cancelled terminal state
            // rather than whatever partial snapshot the cancel response
            // happens to include.
            return await GetOrderAsync(symbol, orderId);
        }

        public async Task<IEnumerable<ExchangeOrder>> GetOpenOrdersAsync()
        {
            // Null symbol returns open orders across all margin pairs.
            var result = await _rest.SpotApi.Trading.GetOpenMarginOrdersAsync();
            EnsureSuccess(result, nameof(GetOpenOrdersAsync));
            return result.Data.Select(BinanceNetMapper.ToExchangeOrder);
        }

        #endregion

        #region Market Data (REST)

        public async Task<IEnumerable<ExchangeCandlestick>> GetCandlesticksAsync(
            string symbol, CandleInterval interval, DateTime from, DateTime to)
        {
            // Margin trades on the spot book — candles are identical to spot.
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

        #region Positions & Leverage (margin = no-op)

        public Task<IEnumerable<ExchangePosition>> GetPositionsAsync()
        {
            // Margin doesn't expose signed positions — net exposure lives in
            // the margin balance snapshot (Available - Borrowed per asset).
            // Consumers that need that view can call GetBalancesAsync and
            // read BinanceMarginBalance directly via the REST client.
            return Task.FromResult<IEnumerable<ExchangePosition>>(Array.Empty<ExchangePosition>());
        }

        public Task SetLeverageAsync(string symbol, int leverage)
        {
            // Margin leverage isn't a per-symbol setting — it emerges from
            // how much the account chooses to borrow on each order. Reject
            // obviously invalid values to match the futures provider's
            // "fail loud on bad config" contract, but otherwise no-op.
            if (leverage < 1)
                throw new ArgumentOutOfRangeException(nameof(leverage), leverage,
                    "Margin leverage must be >= 1.");
            return Task.CompletedTask;
        }

        #endregion

        #region User Data Stream

        public async Task SubscribeUserStreamAsync(Action<ExchangeFill> onFill)
        {
            if (onFill == null) throw new ArgumentNullException(nameof(onFill));

            // Margin requires an explicit listen-token fetch before we can
            // subscribe — the SDK does not auto-manage this like it does for
            // the spot stream. First arg null = cross margin (not isolated).
            var tokenResult = await _rest.SpotApi.Account.GetMarginUserListenTokenAsync();
            EnsureSuccess(tokenResult, nameof(SubscribeUserStreamAsync) + ":GetListenToken");
            var listenToken = tokenResult.Data.Token;

            var result = await _socket.SpotApi.Account.SubscribeToMarginUserDataUpdatesAsync(
                listenToken,
                onOrderUpdateMessage: evt =>
                {
                    if (evt.Data == null) return;
                    onFill(BinanceNetMapper.ToExchangeFill(evt.Data));
                },
                onOcoOrderUpdateMessage: null,
                onAccountPositionMessage: null,
                onAccountBalanceUpdate: null,
                onUserDataStreamTerminated: null);
            EnsureSuccess(result, nameof(SubscribeUserStreamAsync));
        }

        #endregion

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
