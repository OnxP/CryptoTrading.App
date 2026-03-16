using System;
using System.Globalization;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Exchange.BitfinexAdapter
{
    /// <summary>
    /// Maps between Bitfinex API formats and exchange-agnostic domain types.
    /// Bitfinex uses different symbol formats (e.g. "tBTCUST" vs "BTCUSDT")
    /// and different API response structures than Binance.
    /// </summary>
    public static class BitfinexMapper
    {
        public const string ExchangeName = "Bitfinex";

        #region Symbol Format Conversion

        /// <summary>
        /// Converts standard ticker format (e.g. "BTCUSDT") to Bitfinex format (e.g. "tBTCUST")
        /// </summary>
        public static string TobitfinexSymbol(string standardTicker)
        {
            if (string.IsNullOrEmpty(standardTicker))
                return standardTicker;

            // Bitfinex uses "t" prefix for trading pairs
            // and truncates USDT to UST
            var symbol = standardTicker.Replace("USDT", "UST");
            return $"t{symbol}";
        }

        /// <summary>
        /// Converts Bitfinex symbol format (e.g. "tBTCUST") to standard format (e.g. "BTCUSDT")
        /// </summary>
        public static string ToStandardSymbol(string bitfinexSymbol)
        {
            if (string.IsNullOrEmpty(bitfinexSymbol))
                return bitfinexSymbol;

            // Remove "t" prefix
            var symbol = bitfinexSymbol.TrimStart('t');

            // Expand UST back to USDT
            if (symbol.EndsWith("UST"))
            {
                symbol = symbol.Substring(0, symbol.Length - 3) + "USDT";
            }

            return symbol;
        }

        /// <summary>
        /// Extract base asset from a standard ticker (e.g. "BTC" from "BTCUSDT")
        /// </summary>
        public static string GetBaseAsset(string standardTicker, string quoteAsset)
        {
            if (standardTicker.EndsWith(quoteAsset))
                return standardTicker.Substring(0, standardTicker.Length - quoteAsset.Length);
            return standardTicker;
        }

        #endregion

        #region Order Mapping

        public static ExchangeOrderSide MapOrderSide(string side)
        {
            // Bitfinex uses positive amount for buy, negative for sell
            // In REST API responses, the "side" may be represented differently
            if (string.Equals(side, "buy", StringComparison.OrdinalIgnoreCase))
                return ExchangeOrderSide.Buy;
            return ExchangeOrderSide.Sell;
        }

        public static ExchangeOrderSide MapOrderSideFromAmount(decimal amount)
        {
            return amount > 0 ? ExchangeOrderSide.Buy : ExchangeOrderSide.Sell;
        }

        public static ExchangeOrderType MapOrderType(string type)
        {
            switch (type?.ToUpperInvariant())
            {
                case "MARKET":
                case "EXCHANGE MARKET":
                    return ExchangeOrderType.Market;
                case "LIMIT":
                case "EXCHANGE LIMIT":
                    return ExchangeOrderType.Limit;
                case "STOP":
                case "EXCHANGE STOP":
                case "STOP LIMIT":
                case "EXCHANGE STOP LIMIT":
                    return ExchangeOrderType.StopLimit;
                default:
                    return ExchangeOrderType.Market;
            }
        }

        public static ExchangeOrderStatus MapOrderStatus(string status)
        {
            if (string.IsNullOrEmpty(status))
                return ExchangeOrderStatus.New;

            var upper = status.ToUpperInvariant();

            if (upper.Contains("ACTIVE"))
                return ExchangeOrderStatus.New;
            if (upper.Contains("PARTIALLY FILLED"))
                return ExchangeOrderStatus.PartiallyFilled;
            if (upper.Contains("EXECUTED") || upper.Contains("FILLED"))
                return ExchangeOrderStatus.Filled;
            if (upper.Contains("CANCELED") || upper.Contains("CANCELLED"))
                return ExchangeOrderStatus.Cancelled;

            return ExchangeOrderStatus.New;
        }

        #endregion

        #region Candlestick Mapping

        /// <summary>
        /// Maps CandleInterval to Bitfinex timeframe string (e.g. "4h", "1m", "1D")
        /// </summary>
        public static string MapCandleIntervalToTimeframe(CandleInterval interval)
        {
            switch (interval)
            {
                case CandleInterval.Minute_1: return "1m";
                case CandleInterval.Minute_3: return "3m";
                case CandleInterval.Minute_5: return "5m";
                case CandleInterval.Minute_15: return "15m";
                case CandleInterval.Minute_30: return "30m";
                case CandleInterval.Hour_1: return "1h";
                case CandleInterval.Hour_2: return "2h";
                case CandleInterval.Hour_4: return "4h";
                case CandleInterval.Hour_6: return "6h";
                case CandleInterval.Hour_8: return "8h";
                case CandleInterval.Hour_12: return "12h";
                case CandleInterval.Day_1: return "1D";
                case CandleInterval.Day_3: return "3D";
                case CandleInterval.Week_1: return "1W";
                case CandleInterval.Month_1: return "1M";
                default: return "1h";
            }
        }

        /// <summary>
        /// Parse Bitfinex candlestick array response into ExchangeCandlestick.
        /// Bitfinex REST v2 candle format: [MTS, OPEN, CLOSE, HIGH, LOW, VOLUME]
        /// </summary>
        public static ExchangeCandlestick ParseCandlestick(decimal[] data, string symbol, CandleInterval interval)
        {
            var openTime = DateTimeOffset.FromUnixTimeMilliseconds((long)data[0]).UtcDateTime;
            var duration = GetIntervalDuration(interval);

            return new ExchangeCandlestick(
                ExchangeName,
                symbol,
                interval,
                openTime,
                openTime.Add(duration),
                data[1],   // Open
                data[3],   // High
                data[4],   // Low
                data[2],   // Close (Bitfinex has Close at index 2, not 3)
                data[5]);  // Volume
        }

        private static TimeSpan GetIntervalDuration(CandleInterval interval)
        {
            switch (interval)
            {
                case CandleInterval.Minute_1: return TimeSpan.FromMinutes(1);
                case CandleInterval.Minute_3: return TimeSpan.FromMinutes(3);
                case CandleInterval.Minute_5: return TimeSpan.FromMinutes(5);
                case CandleInterval.Minute_15: return TimeSpan.FromMinutes(15);
                case CandleInterval.Minute_30: return TimeSpan.FromMinutes(30);
                case CandleInterval.Hour_1: return TimeSpan.FromHours(1);
                case CandleInterval.Hour_2: return TimeSpan.FromHours(2);
                case CandleInterval.Hour_4: return TimeSpan.FromHours(4);
                case CandleInterval.Hour_6: return TimeSpan.FromHours(6);
                case CandleInterval.Hour_8: return TimeSpan.FromHours(8);
                case CandleInterval.Hour_12: return TimeSpan.FromHours(12);
                case CandleInterval.Day_1: return TimeSpan.FromDays(1);
                case CandleInterval.Day_3: return TimeSpan.FromDays(3);
                case CandleInterval.Week_1: return TimeSpan.FromDays(7);
                case CandleInterval.Month_1: return TimeSpan.FromDays(30);
                default: return TimeSpan.FromHours(1);
            }
        }

        #endregion

        #region Timestamp Helpers

        public static long ToUnixMilliseconds(DateTime dateTime)
        {
            return new DateTimeOffset(dateTime.ToUniversalTime()).ToUnixTimeMilliseconds();
        }

        public static DateTime FromUnixMilliseconds(long milliseconds)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
        }

        #endregion
    }
}
