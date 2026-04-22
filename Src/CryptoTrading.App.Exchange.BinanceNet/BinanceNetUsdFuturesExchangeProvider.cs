using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Binance.Net.Interfaces.Clients;
using CryptoTrading.App.Core.Exchange;
// As in the spot provider, avoid the Binance.Net.Enums import-wide to keep
// the neutral PositionSide from colliding; alias only what we use.
using BinanceTimeInForce = Binance.Net.Enums.TimeInForce;
using BinanceFuturesOrderType = Binance.Net.Enums.FuturesOrderType;
using BinancePositionSide = Binance.Net.Enums.PositionSide;

namespace CryptoTrading.App.Exchange.BinanceNet
{
    /// <summary>
    /// Binance.Net-backed IExchangeProvider for USD-M PERPETUAL FUTURES. All
    /// calls route through <c>_rest.UsdFuturesApi</c> / <c>_socket.UsdFuturesApi</c>
    /// so spot and futures configurations can coexist side-by-side in the
    /// same composition root with different venue selections.
    ///
    /// Futures semantics differ from spot in three important ways; each is
    /// reflected below:
    ///   1. Balances are per-margin-asset with a single "AvailableBalance"
    ///      (no Free/Locked split); unrealized PnL lives on positions.
    ///   2. Leverage is per-symbol and must be bootstrapped before placing
    ///      the first order — <see cref="SetLeverageAsync"/>.
    ///   3. Position direction is carried explicitly as PositionSide; the
    ///      algorithm passes Long/Short in dual-mode, Both in one-way mode.
    /// Margin side-effects do not apply to derivatives and are rejected.
    /// </summary>
    public class BinanceNetUsdFuturesExchangeProvider : IExchangeProvider
    {
        private readonly IBinanceRestClient _rest;
        private readonly IBinanceSocketClient _socket;

        public string ExchangeId => BinanceNetMapper.ExchangeName;

        public TradingVenue Venue => TradingVenue.Futures;

        public BinanceNetUsdFuturesExchangeProvider(IBinanceRestClient rest, IBinanceSocketClient socket)
        {
            _rest = rest ?? throw new ArgumentNullException(nameof(rest));
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        }

        #region Account

        public async Task<IEnumerable<ExchangeBalance>> GetBalancesAsync()
        {
            var result = await _rest.UsdFuturesApi.Account.GetBalancesAsync();
            EnsureSuccess(result, nameof(GetBalancesAsync));
            return result.Data
                // Filter out zero-balance assets so downstream balance
                // snapshots don't fill with noise for the 20+ supported
                // margin assets most accounts don't hold.
                .Where(b => b.WalletBalance > 0 || b.AvailableBalance > 0)
                .Select(BinanceNetMapper.ToExchangeBalance);
        }

        public async Task<IEnumerable<ExchangeSymbol>> GetSymbolsAsync()
        {
            var result = await _rest.UsdFuturesApi.ExchangeData.GetExchangeInfoAsync();
            EnsureSuccess(result, nameof(GetSymbolsAsync));
            return result.Data.Symbols.Select(BinanceNetMapper.ToExchangeSymbol);
        }

        public Task<ExchangeFeeSchedule> GetFeeScheduleAsync()
        {
            // Binance USD-M standard taker/maker is 0.02%/0.04% at the base
            // VIP-0 tier. BNB discount applies if the account has opted in;
            // surfacing the standard schedule matches the spot provider's
            // behaviour and the per-symbol override can be fetched via the
            // exchange's commission-rate endpoint by callers that care.
            var schedule = new ExchangeFeeSchedule(ExchangeId, 0.0002m, 0.0004m, "BNB")
            {
                HasFeeDiscount = true,
                DiscountRate = 0.10m
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
            GuardNonFuturesParams(marginSideEffect);
            var result = await _rest.UsdFuturesApi.Trading.PlaceOrderAsync(
                symbol,
                BinanceNetMapper.MapToBinanceOrderSide(side),
                BinanceFuturesOrderType.Market,
                quantity: quantity,
                positionSide: BinanceNetMapper.MapToBinancePositionSide(positionSide),
                reduceOnly: reduceOnly);
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
            GuardNonFuturesParams(marginSideEffect);
            var result = await _rest.UsdFuturesApi.Trading.PlaceOrderAsync(
                symbol,
                BinanceNetMapper.MapToBinanceOrderSide(side),
                BinanceFuturesOrderType.Limit,
                quantity: quantity,
                price: price,
                positionSide: BinanceNetMapper.MapToBinancePositionSide(positionSide),
                timeInForce: BinanceTimeInForce.GoodTillCanceled,
                reduceOnly: reduceOnly);
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
            GuardNonFuturesParams(marginSideEffect);
            // Futures uses FuturesOrderType.Stop for stop-limit (Stop vs
            // StopMarket in Binance parlance); we intentionally pick the
            // limit variant because the neutral contract exposes a limit
            // price — callers that want stop-market should use MarketOrder
            // with reduceOnly=true after detecting the trigger.
            var result = await _rest.UsdFuturesApi.Trading.PlaceOrderAsync(
                symbol,
                BinanceNetMapper.MapToBinanceOrderSide(side),
                BinanceFuturesOrderType.Stop,
                quantity: quantity,
                price: limitPrice,
                stopPrice: stopPrice,
                positionSide: BinanceNetMapper.MapToBinancePositionSide(positionSide),
                timeInForce: BinanceTimeInForce.GoodTillCanceled,
                reduceOnly: reduceOnly);
            EnsureSuccess(result, nameof(PlaceStopLimitOrderAsync));
            return BinanceNetMapper.ToExchangeOrder(result.Data);
        }

        /// <summary>
        /// Futures rejects <see cref="MarginSideEffect"/> — that's a margin-
        /// venue-only concept. Fail loud so misrouted orders surface here
        /// rather than silently dropping the auto-borrow flag and quietly
        /// failing at the exchange.
        /// </summary>
        private static void GuardNonFuturesParams(MarginSideEffect marginSideEffect)
        {
            if (marginSideEffect != MarginSideEffect.None)
                throw new NotSupportedException(
                    $"{nameof(BinanceNetUsdFuturesExchangeProvider)} does not support marginSideEffect={marginSideEffect}. " +
                    "Use the margin provider (PR 4+).");
        }

        public async Task<ExchangeOrder> GetOrderAsync(string symbol, string orderId)
        {
            if (long.TryParse(orderId, out var id))
            {
                var result = await _rest.UsdFuturesApi.Trading.GetOrderAsync(symbol, orderId: id);
                EnsureSuccess(result, nameof(GetOrderAsync));
                return BinanceNetMapper.ToExchangeOrder(result.Data);
            }

            var byClient = await _rest.UsdFuturesApi.Trading.GetOrderAsync(symbol, origClientOrderId: orderId);
            EnsureSuccess(byClient, nameof(GetOrderAsync));
            return BinanceNetMapper.ToExchangeOrder(byClient.Data);
        }

        public async Task<ExchangeOrder> CancelOrderAsync(string symbol, string orderId)
        {
            if (long.TryParse(orderId, out var id))
            {
                var cancel = await _rest.UsdFuturesApi.Trading.CancelOrderAsync(symbol, orderId: id);
                EnsureSuccess(cancel, nameof(CancelOrderAsync));
            }
            else
            {
                var cancel = await _rest.UsdFuturesApi.Trading.CancelOrderAsync(symbol, origClientOrderId: orderId);
                EnsureSuccess(cancel, nameof(CancelOrderAsync));
            }
            return await GetOrderAsync(symbol, orderId);
        }

        public async Task<IEnumerable<ExchangeOrder>> GetOpenOrdersAsync()
        {
            var result = await _rest.UsdFuturesApi.Trading.GetOpenOrdersAsync();
            EnsureSuccess(result, nameof(GetOpenOrdersAsync));
            return result.Data.Select(BinanceNetMapper.ToExchangeOrder);
        }

        #endregion

        #region Market Data (REST)

        public async Task<IEnumerable<ExchangeCandlestick>> GetCandlesticksAsync(
            string symbol, CandleInterval interval, DateTime from, DateTime to)
        {
            var binanceInterval = BinanceNetMapper.MapToBinanceKlineInterval(interval);
            var result = await _rest.UsdFuturesApi.ExchangeData.GetKlinesAsync(symbol, binanceInterval, from, to);
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
            var result = await _socket.UsdFuturesApi.ExchangeData.SubscribeToKlineUpdatesAsync(
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

        #region Positions & Leverage

        public async Task<IEnumerable<ExchangePosition>> GetPositionsAsync()
        {
            var result = await _rest.UsdFuturesApi.Account.GetPositionInformationAsync();
            EnsureSuccess(result, nameof(GetPositionsAsync));
            // Binance returns one row per (symbol, positionSide) even when
            // flat; callers are typically interested in open exposure, but
            // we surface everything so the consumer can filter by IsFlat.
            // Explicit lambda avoids a method-group overload-inference
            // failure when Binance.Net changes the positionRisk row type
            // (there have been several — V2 / Info / V3 across versions).
            return result.Data.Select(p => BinanceNetMapper.ToExchangePosition(p));
        }

        public async Task SetLeverageAsync(string symbol, int leverage)
        {
            if (leverage < 1)
                throw new ArgumentOutOfRangeException(nameof(leverage), leverage,
                    "Leverage must be a positive integer.");
            var result = await _rest.UsdFuturesApi.Account.ChangeInitialLeverageAsync(symbol, leverage);
            EnsureSuccess(result, nameof(SetLeverageAsync));
        }

        #endregion

        #region User Data Stream

        public async Task SubscribeUserStreamAsync(Action<ExchangeFill> onFill)
        {
            if (onFill == null) throw new ArgumentNullException(nameof(onFill));

            // Futures requires an explicit listen-key bootstrap — unlike
            // the spot socket client, the USD-M socket does NOT auto-manage
            // the stream key. Start one here so consumers don't have to.
            // (We rely on the socket client's own keepalive once subscribed.)
            var listenKeyResult = await _rest.UsdFuturesApi.Account.StartUserStreamAsync();
            EnsureSuccess(listenKeyResult, nameof(SubscribeUserStreamAsync) + "(StartUserStream)");

            var sub = await _socket.UsdFuturesApi.Account.SubscribeToUserDataUpdatesAsync(
                listenKeyResult.Data,
                onLeverageUpdate: null,
                onMarginUpdate: null,
                onAccountUpdate: null,
                onOrderUpdate: evt =>
                {
                    if (evt.Data == null) return;
                    onFill(BinanceNetMapper.ToExchangeFill(evt.Data));
                },
                onTradeUpdate: null,
                onListenKeyExpired: null,
                onStrategyUpdate: null,
                onGridUpdate: null,
                onConditionalOrderTriggerRejectUpdate: null,
                onAlgoOrderUpdate: null);
            EnsureSuccess(sub, nameof(SubscribeUserStreamAsync));
        }

        #endregion

        /// <summary>
        /// Binance.Net wraps every call in a WebCallResult; non-Success
        /// responses carry the error code/message. Translate them to a
        /// single InvalidOperationException so the caller doesn't have to
        /// reach into CryptoExchange.Net types.
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
