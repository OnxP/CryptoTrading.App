using System;
using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Objects.Models.Futures;
using Binance.Net.Objects.Models.Futures.Socket;
using Binance.Net.Objects.Models.Spot;
using Binance.Net.Objects.Models.Spot.Socket;
using CryptoTrading.App.Core.Exchange;
using BinanceSpotOrderType = Binance.Net.Enums.SpotOrderType;
// Neutral PositionSide lives in CryptoTrading.App.Core.Exchange; Binance.Net
// ships its own enum of the same name. Alias the Binance one so futures
// mappings can convert cleanly without renaming the neutral side anywhere.
using BinancePositionSide = Binance.Net.Enums.PositionSide;

namespace CryptoTrading.App.Exchange.BinanceNet
{
    /// <summary>
    /// Maps between Binance.Net SDK types and the exchange-agnostic domain
    /// types. Parallel to <c>BinanceMapper</c> in the bundled-SDK adapter —
    /// the two live side by side until the bundled SDK is removed in PR 6.
    /// </summary>
    public static class BinanceNetMapper
    {
        // Keep the neutral ExchangeId identical to the bundled adapter so
        // downstream consumers (balance snapshots, fee schedules, etc.) see
        // a single "Binance" exchange regardless of which SDK is driving it.
        public const string ExchangeName = "Binance";

        #region Order mapping

        public static ExchangeOrder ToExchangeOrder(BinancePlacedOrder order)
        {
            // Fresh place responses usually populate TransactTime but not
            // CreateTime; coalesce so consumers never see a null timestamp.
            return new ExchangeOrder
            {
                ExchangeId = ExchangeName,
                OrderId = order.Id.ToString(),
                ClientOrderId = order.ClientOrderId,
                Symbol = order.Symbol,
                Side = MapOrderSide(order.Side),
                Type = MapOrderType(order.Type),
                Status = MapOrderStatus(order.Status),
                Price = order.Price,
                StopPrice = order.StopPrice ?? 0m,
                Quantity = order.Quantity,
                FilledQuantity = order.QuantityFilled,
                QuoteQuantity = order.QuoteQuantityFilled,
                Timestamp = order.TransactTime
                    ?? (order.CreateTime != default ? order.CreateTime : DateTime.UtcNow)
            };
        }

        public static ExchangeOrder ToExchangeOrder(BinanceOrder order)
        {
            return new ExchangeOrder
            {
                ExchangeId = ExchangeName,
                OrderId = order.Id.ToString(),
                ClientOrderId = order.ClientOrderId,
                Symbol = order.Symbol,
                Side = MapOrderSide(order.Side),
                Type = MapOrderType(order.Type),
                Status = MapOrderStatus(order.Status),
                Price = order.Price,
                StopPrice = order.StopPrice ?? 0m,
                Quantity = order.Quantity,
                FilledQuantity = order.QuantityFilled,
                QuoteQuantity = order.QuoteQuantityFilled,
                Timestamp = order.CreateTime
            };
        }

        public static ExchangeOrderSide MapOrderSide(OrderSide side)
        {
            return side == OrderSide.Buy ? ExchangeOrderSide.Buy : ExchangeOrderSide.Sell;
        }

        public static OrderSide MapToBinanceOrderSide(ExchangeOrderSide side)
        {
            return side == ExchangeOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell;
        }

        public static ExchangeOrderType MapOrderType(BinanceSpotOrderType type)
        {
            switch (type)
            {
                case BinanceSpotOrderType.Market:
                    return ExchangeOrderType.Market;
                case BinanceSpotOrderType.Limit:
                case BinanceSpotOrderType.LimitMaker:
                    return ExchangeOrderType.Limit;
                case BinanceSpotOrderType.StopLoss:
                case BinanceSpotOrderType.StopLossLimit:
                case BinanceSpotOrderType.TakeProfit:
                case BinanceSpotOrderType.TakeProfitLimit:
                    return ExchangeOrderType.StopLimit;
                default:
                    return ExchangeOrderType.Market;
            }
        }

        public static ExchangeOrderStatus MapOrderStatus(OrderStatus status)
        {
            switch (status)
            {
                case OrderStatus.New:
                    return ExchangeOrderStatus.New;
                case OrderStatus.PartiallyFilled:
                    return ExchangeOrderStatus.PartiallyFilled;
                case OrderStatus.Filled:
                    return ExchangeOrderStatus.Filled;
                case OrderStatus.Canceled:
                case OrderStatus.PendingCancel:
                    return ExchangeOrderStatus.Cancelled;
                case OrderStatus.Rejected:
                    return ExchangeOrderStatus.Rejected;
                case OrderStatus.Expired:
                case OrderStatus.ExpiredInMatch:
                    return ExchangeOrderStatus.Expired;
                default:
                    return ExchangeOrderStatus.New;
            }
        }

        #endregion

        #region Balance mapping

        public static ExchangeBalance ToExchangeBalance(BinanceBalance balance)
        {
            return new ExchangeBalance(ExchangeName, balance.Asset, balance.Available, balance.Locked);
        }

        #endregion

        #region Symbol mapping

        public static ExchangeSymbol ToExchangeSymbol(BinanceSymbol symbol)
        {
            var exchangeSymbol = new ExchangeSymbol(
                ExchangeName,
                symbol.Name,
                symbol.BaseAsset,
                symbol.QuoteAsset)
            {
                IsActive = symbol.Status == SymbolStatus.Trading
            };

            // Binance encodes step sizes + notional minimums as per-symbol
            // filter rows; copy the subset our algorithms need. A filter being
            // absent is not an error — some symbols simply don't publish one.
            if (symbol.LotSizeFilter != null)
            {
                exchangeSymbol.MinQuantity = symbol.LotSizeFilter.MinQuantity;
                exchangeSymbol.MaxQuantity = symbol.LotSizeFilter.MaxQuantity;
                exchangeSymbol.StepSize = symbol.LotSizeFilter.StepSize;
            }
            if (symbol.PriceFilter != null)
            {
                exchangeSymbol.TickSize = symbol.PriceFilter.TickSize;
            }
            if (symbol.MinNotionalFilter != null)
            {
                exchangeSymbol.MinNotional = symbol.MinNotionalFilter.MinNotional;
            }
            else if (symbol.NotionalFilter != null)
            {
                exchangeSymbol.MinNotional = symbol.NotionalFilter.MinNotional;
            }

            return exchangeSymbol;
        }

        #endregion

        #region Candlestick mapping

        public static ExchangeCandlestick ToExchangeCandlestick(IBinanceKline kline, string symbol, CandleInterval interval)
        {
            return new ExchangeCandlestick(
                ExchangeName,
                symbol,
                interval,
                kline.OpenTime,
                kline.CloseTime,
                kline.OpenPrice,
                kline.HighPrice,
                kline.LowPrice,
                kline.ClosePrice,
                kline.Volume)
            {
                QuoteVolume = kline.QuoteVolume,
                NumberOfTrades = kline.TradeCount
            };
        }

        public static ExchangeCandlestick ToExchangeCandlestick(IBinanceStreamKline streamKline, string symbol)
        {
            // IBinanceStreamKline carries its own Interval, so no
            // out-of-band lookup is needed. It also inherits the IBinanceKline
            // OHLC surface, so the same field names work for REST + stream.
            return new ExchangeCandlestick(
                ExchangeName,
                symbol,
                MapCandleInterval(streamKline.Interval),
                streamKline.OpenTime,
                streamKline.CloseTime,
                streamKline.OpenPrice,
                streamKline.HighPrice,
                streamKline.LowPrice,
                streamKline.ClosePrice,
                streamKline.Volume)
            {
                QuoteVolume = streamKline.QuoteVolume,
                NumberOfTrades = streamKline.TradeCount,
                IsClosed = streamKline.Final
            };
        }

        public static CandleInterval MapCandleInterval(KlineInterval interval)
        {
            switch (interval)
            {
                case KlineInterval.OneMinute: return CandleInterval.Minute_1;
                case KlineInterval.ThreeMinutes: return CandleInterval.Minute_3;
                case KlineInterval.FiveMinutes: return CandleInterval.Minute_5;
                case KlineInterval.FifteenMinutes: return CandleInterval.Minute_15;
                case KlineInterval.ThirtyMinutes: return CandleInterval.Minute_30;
                case KlineInterval.OneHour: return CandleInterval.Hour_1;
                case KlineInterval.TwoHour: return CandleInterval.Hour_2;
                case KlineInterval.FourHour: return CandleInterval.Hour_4;
                case KlineInterval.SixHour: return CandleInterval.Hour_6;
                case KlineInterval.EightHour: return CandleInterval.Hour_8;
                case KlineInterval.TwelveHour: return CandleInterval.Hour_12;
                case KlineInterval.OneDay: return CandleInterval.Day_1;
                case KlineInterval.ThreeDay: return CandleInterval.Day_3;
                case KlineInterval.OneWeek: return CandleInterval.Week_1;
                case KlineInterval.OneMonth: return CandleInterval.Month_1;
                default: return CandleInterval.Hour_1;
            }
        }

        public static KlineInterval MapToBinanceKlineInterval(CandleInterval interval)
        {
            switch (interval)
            {
                case CandleInterval.Minute_1: return KlineInterval.OneMinute;
                case CandleInterval.Minute_3: return KlineInterval.ThreeMinutes;
                case CandleInterval.Minute_5: return KlineInterval.FiveMinutes;
                case CandleInterval.Minute_15: return KlineInterval.FifteenMinutes;
                case CandleInterval.Minute_30: return KlineInterval.ThirtyMinutes;
                case CandleInterval.Hour_1: return KlineInterval.OneHour;
                case CandleInterval.Hour_2: return KlineInterval.TwoHour;
                case CandleInterval.Hour_4: return KlineInterval.FourHour;
                case CandleInterval.Hour_6: return KlineInterval.SixHour;
                case CandleInterval.Hour_8: return KlineInterval.EightHour;
                case CandleInterval.Hour_12: return KlineInterval.TwelveHour;
                case CandleInterval.Day_1: return KlineInterval.OneDay;
                case CandleInterval.Day_3: return KlineInterval.ThreeDay;
                case CandleInterval.Week_1: return KlineInterval.OneWeek;
                case CandleInterval.Month_1: return KlineInterval.OneMonth;
                default: return KlineInterval.OneHour;
            }
        }

        #endregion

        #region Futures Order / Position / Balance mapping

        /// <summary>
        /// Map a USD-M futures order (BinanceFuturesOrder) to the neutral
        /// ExchangeOrder. Shared by REST place / get / cancel responses.
        /// </summary>
        public static ExchangeOrder ToExchangeOrder(BinanceFuturesOrder order)
        {
            return new ExchangeOrder
            {
                ExchangeId = ExchangeName,
                OrderId = order.Id.ToString(),
                ClientOrderId = order.ClientOrderId,
                Symbol = order.Symbol,
                Side = MapOrderSide(order.Side),
                Type = MapFuturesOrderType(order.Type),
                Status = MapOrderStatus(order.Status),
                Price = order.Price,
                // StopPrice on futures is nullable (only set for stop/TP
                // variants); coalesce to 0 so the neutral field matches the
                // convention of the spot mapping above.
                StopPrice = order.StopPrice ?? 0m,
                Quantity = order.Quantity,
                FilledQuantity = order.QuantityFilled,
                QuoteQuantity = order.QuoteQuantityFilled ?? 0m,
                // Futures CreateTime is documented to be populated on every
                // response — unlike the spot BinancePlacedOrder where it's
                // nullable and sometimes empty on fresh placements.
                Timestamp = order.CreateTime
            };
        }

        public static ExchangeOrderType MapFuturesOrderType(FuturesOrderType type)
        {
            switch (type)
            {
                case FuturesOrderType.Market:
                case FuturesOrderType.StopMarket:
                case FuturesOrderType.TakeProfitMarket:
                case FuturesOrderType.TrailingStopMarket:
                case FuturesOrderType.Liquidation:
                    return ExchangeOrderType.Market;
                case FuturesOrderType.Limit:
                    return ExchangeOrderType.Limit;
                case FuturesOrderType.Stop:
                case FuturesOrderType.TakeProfit:
                    return ExchangeOrderType.StopLimit;
                default:
                    return ExchangeOrderType.Market;
            }
        }

        /// <summary>
        /// Pick the USD-M futures enum value to send on PlaceOrder. Futures
        /// has no "StopLimit" — it has Stop (stop-limit) and StopMarket
        /// (stop-market); we mirror the bundled adapter by emitting the
        /// limit variants unless the caller asked for a market order.
        /// </summary>
        public static FuturesOrderType MapToBinanceFuturesOrderType(ExchangeOrderType type)
        {
            switch (type)
            {
                case ExchangeOrderType.Market: return FuturesOrderType.Market;
                case ExchangeOrderType.Limit: return FuturesOrderType.Limit;
                case ExchangeOrderType.StopLimit: return FuturesOrderType.Stop;
                default: return FuturesOrderType.Market;
            }
        }

        public static BinancePositionSide MapToBinancePositionSide(Core.Exchange.PositionSide side)
        {
            switch (side)
            {
                case Core.Exchange.PositionSide.Long: return BinancePositionSide.Long;
                case Core.Exchange.PositionSide.Short: return BinancePositionSide.Short;
                default: return BinancePositionSide.Both;
            }
        }

        public static Core.Exchange.PositionSide MapFromBinancePositionSide(BinancePositionSide side)
        {
            switch (side)
            {
                case BinancePositionSide.Long: return Core.Exchange.PositionSide.Long;
                case BinancePositionSide.Short: return Core.Exchange.PositionSide.Short;
                default: return Core.Exchange.PositionSide.Both;
            }
        }

        /// <summary>
        /// Map one /fapi/v2/positionRisk row (BinancePositionDetailsUsdt) to
        /// a neutral ExchangePosition. Binance returns one row per
        /// (symbol, positionSide) even when flat; callers are typically
        /// interested in open exposure and can filter via
        /// <see cref="ExchangePosition.IsFlat"/>.
        /// </summary>
        public static ExchangePosition ToExchangePosition(BinancePositionDetailsUsdt position)
        {
            return new ExchangePosition
            {
                ExchangeId = ExchangeName,
                Symbol = position.Symbol,
                Side = MapFromBinancePositionSide(position.PositionSide),
                Quantity = position.Quantity,
                EntryPrice = position.EntryPrice,
                MarkPrice = position.MarkPrice,
                Leverage = (int)position.Leverage,
                UnrealizedPnl = position.UnrealizedPnl,
                Notional = position.Notional,
                Timestamp = position.UpdateTime
            };
        }

        /// <summary>
        /// Map a USD-M futures balance row. Futures has a single free-margin
        /// number (AvailableBalance) rather than the spot Free/Locked split,
        /// so Locked = 0. Consumers that need unrealized PnL should read
        /// positions directly.
        /// </summary>
        public static ExchangeBalance ToExchangeBalance(BinanceFuturesAccountBalance balance)
        {
            return new ExchangeBalance(ExchangeName, balance.Asset, balance.AvailableBalance, 0m);
        }

        /// <summary>
        /// Map a USD-M futures symbol definition. Reuses the same filter
        /// extraction as spot so sizing logic doesn't need to special-case.
        /// Futures symbols don't expose a Status field on this SDK version,
        /// so IsActive defaults to true — delivery dates can be checked via
        /// the DeliveryDate raw field if callers need to filter expired.
        /// </summary>
        public static ExchangeSymbol ToExchangeSymbol(BinanceFuturesSymbol symbol)
        {
            var exchangeSymbol = new ExchangeSymbol(
                ExchangeName,
                symbol.Name,
                symbol.BaseAsset,
                symbol.QuoteAsset);

            if (symbol.LotSizeFilter != null)
            {
                exchangeSymbol.MinQuantity = symbol.LotSizeFilter.MinQuantity;
                exchangeSymbol.MaxQuantity = symbol.LotSizeFilter.MaxQuantity;
                exchangeSymbol.StepSize = symbol.LotSizeFilter.StepSize;
            }
            if (symbol.PriceFilter != null)
            {
                exchangeSymbol.TickSize = symbol.PriceFilter.TickSize;
            }
            if (symbol.MinNotionalFilter != null)
            {
                exchangeSymbol.MinNotional = symbol.MinNotionalFilter.MinNotional;
            }

            return exchangeSymbol;
        }

        /// <summary>
        /// Map a futures user-stream order-update event to a neutral fill.
        /// The inner UpdateData carries the per-execution fields (last
        /// filled qty/price, fee, fee asset, timestamp); TradeId &gt; 0 is
        /// the exchange's signal that this event corresponds to an actual
        /// fill rather than a lifecycle-only transition (NEW/CANCELED).
        /// </summary>
        public static ExchangeFill ToExchangeFill(BinanceFuturesStreamOrderUpdate update)
        {
            var data = update.UpdateData;
            return new ExchangeFill
            {
                ExchangeId = ExchangeName,
                OrderId = data.OrderId.ToString(),
                ClientOrderId = data.ClientOrderId,
                Symbol = data.Symbol,
                Side = MapOrderSide(data.Side),
                Status = MapOrderStatus(data.Status),
                Price = data.PriceLastFilledTrade,
                FilledQuantity = data.QuantityOfLastFilledTrade,
                Commission = data.Fee,
                CommissionAsset = data.FeeAsset,
                Timestamp = data.UpdateTime
            };
        }

        #endregion

        #region Fill (user-stream) mapping

        public static ExchangeFill ToExchangeFill(BinanceStreamOrderUpdate update)
        {
            return new ExchangeFill
            {
                ExchangeId = ExchangeName,
                OrderId = update.Id.ToString(),
                ClientOrderId = update.ClientOrderId,
                Symbol = update.Symbol,
                Side = MapOrderSide(update.Side),
                Status = MapOrderStatus(update.Status),
                Price = update.LastPriceFilled,
                FilledQuantity = update.LastQuantityFilled,
                Commission = update.Fee,
                CommissionAsset = update.FeeAsset,
                Timestamp = update.UpdateTime
            };
        }

        #endregion
    }
}
