using System;
using System.Collections.Generic;
using Binance;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Exchange.BinanceAdapter
{
    /// <summary>
    /// Maps between Binance SDK types and exchange-agnostic domain types.
    /// </summary>
    public static class BinanceMapper
    {
        public const string ExchangeName = "Binance";

        #region Order Mapping

        public static ExchangeOrder ToExchangeOrder(Order order)
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
                StopPrice = order.StopPrice,
                Quantity = order.OriginalQuantity,
                FilledQuantity = order.ExecutedQuantity,
                QuoteQuantity = order.CummulativeQuoteAssetQuantity,
                Timestamp = order.Time
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

        public static ExchangeOrderType MapOrderType(OrderType type)
        {
            switch (type)
            {
                case OrderType.Market:
                    return ExchangeOrderType.Market;
                case OrderType.Limit:
                case OrderType.LimitMaker:
                    return ExchangeOrderType.Limit;
                case OrderType.StopLossLimit:
                case OrderType.TakeProfitLimit:
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
                    return ExchangeOrderStatus.Expired;
                default:
                    return ExchangeOrderStatus.New;
            }
        }

        #endregion

        #region Balance Mapping

        public static ExchangeBalance ToExchangeBalance(AccountBalance balance)
        {
            return new ExchangeBalance(ExchangeName, balance.Asset, balance.Free, balance.Locked);
        }

        #endregion

        #region Symbol Mapping

        public static ExchangeSymbol ToExchangeSymbol(Symbol symbol)
        {
            return new ExchangeSymbol(
                ExchangeName,
                $"{symbol.BaseAsset}{symbol.QuoteAsset}",
                symbol.BaseAsset.ToString(),
                symbol.QuoteAsset.ToString())
            {
                MinQuantity = symbol.Quantity.Minimum,
                MaxQuantity = symbol.Quantity.Maximum,
                StepSize = symbol.Quantity.Increment,
                MinNotional = symbol.NotionalMinimumValue,
                TickSize = symbol.Price.Increment,
                IsActive = symbol.Status == SymbolStatus.Trading
            };
        }

        #endregion

        #region Candlestick Mapping

        public static ExchangeCandlestick ToExchangeCandlestick(Candlestick candle)
        {
            return new ExchangeCandlestick(
                ExchangeName,
                candle.Symbol,
                MapCandleInterval(candle.Interval),
                candle.OpenTime,
                candle.CloseTime,
                candle.Open,
                candle.High,
                candle.Low,
                candle.Close,
                candle.Volume)
            {
                QuoteVolume = candle.QuoteAssetVolume,
                NumberOfTrades = candle.NumberOfTrades
            };
        }

        public static CandleInterval MapCandleInterval(CandlestickInterval interval)
        {
            switch (interval)
            {
                case CandlestickInterval.Minute: return CandleInterval.Minute_1;
                case CandlestickInterval.Minutes_3: return CandleInterval.Minute_3;
                case CandlestickInterval.Minutes_5: return CandleInterval.Minute_5;
                case CandlestickInterval.Minutes_15: return CandleInterval.Minute_15;
                case CandlestickInterval.Minutes_30: return CandleInterval.Minute_30;
                case CandlestickInterval.Hour: return CandleInterval.Hour_1;
                case CandlestickInterval.Hours_2: return CandleInterval.Hour_2;
                case CandlestickInterval.Hours_4: return CandleInterval.Hour_4;
                case CandlestickInterval.Hours_6: return CandleInterval.Hour_6;
                case CandlestickInterval.Hours_8: return CandleInterval.Hour_8;
                case CandlestickInterval.Hours_12: return CandleInterval.Hour_12;
                case CandlestickInterval.Day: return CandleInterval.Day_1;
                case CandlestickInterval.Days_3: return CandleInterval.Day_3;
                case CandlestickInterval.Week: return CandleInterval.Week_1;
                case CandlestickInterval.Month: return CandleInterval.Month_1;
                default: return CandleInterval.Hour_1;
            }
        }

        public static CandlestickInterval MapToBinanceCandleInterval(CandleInterval interval)
        {
            switch (interval)
            {
                case CandleInterval.Minute_1: return CandlestickInterval.Minute;
                case CandleInterval.Minute_3: return CandlestickInterval.Minutes_3;
                case CandleInterval.Minute_5: return CandlestickInterval.Minutes_5;
                case CandleInterval.Minute_15: return CandlestickInterval.Minutes_15;
                case CandleInterval.Minute_30: return CandlestickInterval.Minutes_30;
                case CandleInterval.Hour_1: return CandlestickInterval.Hour;
                case CandleInterval.Hour_2: return CandlestickInterval.Hours_2;
                case CandleInterval.Hour_4: return CandlestickInterval.Hours_4;
                case CandleInterval.Hour_6: return CandlestickInterval.Hours_6;
                case CandleInterval.Hour_8: return CandlestickInterval.Hours_8;
                case CandleInterval.Hour_12: return CandlestickInterval.Hours_12;
                case CandleInterval.Day_1: return CandlestickInterval.Day;
                case CandleInterval.Day_3: return CandlestickInterval.Days_3;
                case CandleInterval.Week_1: return CandlestickInterval.Week;
                case CandleInterval.Month_1: return CandlestickInterval.Month;
                default: return CandlestickInterval.Hour;
            }
        }

        #endregion
    }
}
