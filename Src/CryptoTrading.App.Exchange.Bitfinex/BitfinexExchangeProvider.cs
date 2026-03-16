using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CryptoTrading.App.Exchange.BitfinexAdapter
{
    /// <summary>
    /// Bitfinex implementation of IExchangeProvider.
    /// Uses Bitfinex REST API v2 and WebSocket v2 for market data.
    /// </summary>
    public class BitfinexExchangeProvider : IExchangeProvider
    {
        private const string BaseUrl = "https://api-pub.bitfinex.com/v2";
        private const string AuthBaseUrl = "https://api.bitfinex.com/v2";

        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _apiSecret;
        private readonly Dictionary<string, Action<ExchangeCandlestick>> _candlestickCallbacks = new Dictionary<string, Action<ExchangeCandlestick>>();

        public string ExchangeId => BitfinexMapper.ExchangeName;

        public BitfinexExchangeProvider(string apiKey, string apiSecret, HttpClient httpClient = null)
        {
            _apiKey = apiKey;
            _apiSecret = apiSecret;
            _httpClient = httpClient ?? new HttpClient();
        }

        #region Account

        public async Task<IEnumerable<ExchangeBalance>> GetBalancesAsync()
        {
            var response = await AuthenticatedPostAsync("/auth/r/wallets");
            var wallets = JArray.Parse(response);

            var balances = new List<ExchangeBalance>();
            foreach (var wallet in wallets)
            {
                var arr = wallet as JArray;
                if (arr == null || arr.Count < 5) continue;

                var walletType = arr[0]?.ToString();
                if (walletType != "exchange") continue; // Only exchange wallets

                var asset = arr[1]?.ToString()?.ToUpperInvariant();
                var balance = arr[2]?.Value<decimal>() ?? 0m;
                var availableBalance = arr[4]?.Value<decimal>() ?? balance;

                if (balance > 0)
                {
                    balances.Add(new ExchangeBalance(
                        ExchangeId,
                        asset,
                        availableBalance,
                        balance - availableBalance));
                }
            }

            return balances;
        }

        public async Task<IEnumerable<ExchangeSymbol>> GetSymbolsAsync()
        {
            var response = await _httpClient.GetStringAsync($"{BaseUrl}/conf/pub:info:pair");
            var data = JArray.Parse(response);

            var symbols = new List<ExchangeSymbol>();
            if (data.Count > 0 && data[0] is JArray pairs)
            {
                foreach (var pair in pairs)
                {
                    var pairArr = pair as JArray;
                    if (pairArr == null || pairArr.Count < 2) continue;

                    var ticker = pairArr[0]?.ToString()?.ToUpperInvariant() ?? "";
                    var info = pairArr[1] as JArray;

                    // Bitfinex pairs are typically 6 chars (e.g. "BTCUSD")
                    string baseAsset, quoteAsset;
                    if (ticker.Length == 6)
                    {
                        baseAsset = ticker.Substring(0, 3);
                        quoteAsset = ticker.Substring(3, 3);
                    }
                    else
                    {
                        baseAsset = ticker;
                        quoteAsset = "USD";
                    }

                    symbols.Add(new ExchangeSymbol(ExchangeId, ticker, baseAsset, quoteAsset)
                    {
                        IsActive = true,
                        MinQuantity = info != null && info.Count > 3 ? info[3]?.Value<decimal>() ?? 0.0001m : 0.0001m,
                        MaxQuantity = info != null && info.Count > 4 ? info[4]?.Value<decimal>() ?? 0m : 0m
                    });
                }
            }

            return symbols;
        }

        public Task<ExchangeFeeSchedule> GetFeeScheduleAsync()
        {
            // Bitfinex standard fees: maker 0.1%, taker 0.2%
            var schedule = new ExchangeFeeSchedule(ExchangeId, 0.001m, 0.002m, "USD");
            return Task.FromResult(schedule);
        }

        #endregion

        #region Orders

        public async Task<ExchangeOrder> PlaceMarketOrderAsync(string symbol, ExchangeOrderSide side, decimal quantity)
        {
            var bfxSymbol = BitfinexMapper.TobitfinexSymbol(symbol);
            var amount = side == ExchangeOrderSide.Buy ? quantity : -quantity;

            var payload = new
            {
                type = "EXCHANGE MARKET",
                symbol = bfxSymbol,
                amount = amount.ToString("F8")
            };

            var response = await AuthenticatedPostAsync("/auth/w/order/submit",
                JsonConvert.SerializeObject(payload));

            var result = JArray.Parse(response);
            return ParseOrderResponse(result, symbol);
        }

        public async Task<ExchangeOrder> PlaceLimitOrderAsync(string symbol, ExchangeOrderSide side, decimal price, decimal quantity)
        {
            var bfxSymbol = BitfinexMapper.TobitfinexSymbol(symbol);
            var amount = side == ExchangeOrderSide.Buy ? quantity : -quantity;

            var payload = new
            {
                type = "EXCHANGE LIMIT",
                symbol = bfxSymbol,
                amount = amount.ToString("F8"),
                price = price.ToString("F8")
            };

            var response = await AuthenticatedPostAsync("/auth/w/order/submit",
                JsonConvert.SerializeObject(payload));

            var result = JArray.Parse(response);
            return ParseOrderResponse(result, symbol);
        }

        public async Task<ExchangeOrder> PlaceStopLimitOrderAsync(string symbol, ExchangeOrderSide side, decimal stopPrice, decimal limitPrice, decimal quantity)
        {
            var bfxSymbol = BitfinexMapper.TobitfinexSymbol(symbol);
            var amount = side == ExchangeOrderSide.Buy ? quantity : -quantity;

            var payload = new
            {
                type = "EXCHANGE STOP LIMIT",
                symbol = bfxSymbol,
                amount = amount.ToString("F8"),
                price = limitPrice.ToString("F8"),
                price_aux_limit = stopPrice.ToString("F8")
            };

            var response = await AuthenticatedPostAsync("/auth/w/order/submit",
                JsonConvert.SerializeObject(payload));

            var result = JArray.Parse(response);
            return ParseOrderResponse(result, symbol);
        }

        public async Task<ExchangeOrder> GetOrderAsync(string symbol, string orderId)
        {
            var payload = new { id = new[] { long.Parse(orderId) } };
            var response = await AuthenticatedPostAsync("/auth/r/orders",
                JsonConvert.SerializeObject(payload));

            var orders = JArray.Parse(response);
            if (orders.Count > 0 && orders[0] is JArray orderArr)
            {
                return ParseOrder(orderArr, symbol);
            }

            return null;
        }

        public async Task<ExchangeOrder> CancelOrderAsync(string symbol, string orderId)
        {
            var payload = new { id = long.Parse(orderId) };
            await AuthenticatedPostAsync("/auth/w/order/cancel",
                JsonConvert.SerializeObject(payload));

            return new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = orderId,
                Symbol = symbol,
                Status = ExchangeOrderStatus.Cancelled,
                Timestamp = DateTime.UtcNow
            };
        }

        public async Task<IEnumerable<ExchangeOrder>> GetOpenOrdersAsync()
        {
            var response = await AuthenticatedPostAsync("/auth/r/orders");
            var orders = JArray.Parse(response);

            return orders
                .OfType<JArray>()
                .Select(o => ParseOrder(o, BitfinexMapper.ToStandardSymbol(o[3]?.ToString() ?? "")))
                .ToList();
        }

        #endregion

        #region Market Data

        public async Task<IEnumerable<ExchangeCandlestick>> GetCandlesticksAsync(string symbol, CandleInterval interval, DateTime from, DateTime to)
        {
            var bfxSymbol = BitfinexMapper.TobitfinexSymbol(symbol);
            var timeframe = BitfinexMapper.MapCandleIntervalToTimeframe(interval);
            var startMs = BitfinexMapper.ToUnixMilliseconds(from);
            var endMs = BitfinexMapper.ToUnixMilliseconds(to);

            var url = $"{BaseUrl}/candles/trade:{timeframe}:{bfxSymbol}/hist?start={startMs}&end={endMs}&sort=1&limit=10000";
            var response = await _httpClient.GetStringAsync(url);
            var data = JArray.Parse(response);

            return data
                .OfType<JArray>()
                .Select(c => BitfinexMapper.ParseCandlestick(
                    c.Select(v => v.Value<decimal>()).ToArray(),
                    symbol,
                    interval))
                .ToList();
        }

        public Task SubscribeCandlestickAsync(string symbol, CandleInterval interval, Action<ExchangeCandlestick> onCandle)
        {
            var key = $"{symbol}_{interval}";
            _candlestickCallbacks[key] = onCandle;

            // WebSocket subscription will be implemented with
            // Bitfinex WebSocket v2 API (wss://api-pub.bitfinex.com/ws/2)
            // For now, register the callback for future WebSocket integration
            throw new NotImplementedException(
                "WebSocket candlestick streaming requires Bitfinex WS v2 integration. " +
                "Use REST polling via GetCandlesticksAsync for now.");
        }

        public Task UnsubscribeAllAsync()
        {
            _candlestickCallbacks.Clear();
            return Task.CompletedTask;
        }

        #endregion

        #region Private Helpers

        private async Task<string> AuthenticatedPostAsync(string path, string body = "{}")
        {
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var signature = $"/api/v2{path}{nonce}{body}";

            using (var hmac = new HMACSHA384(Encoding.UTF8.GetBytes(_apiSecret)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signature));
                var sig = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                var request = new HttpRequestMessage(HttpMethod.Post, $"{AuthBaseUrl}{path}")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };

                request.Headers.Add("bfx-nonce", nonce);
                request.Headers.Add("bfx-apikey", _apiKey);
                request.Headers.Add("bfx-signature", sig);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
        }

        private ExchangeOrder ParseOrderResponse(JArray result, string symbol)
        {
            // Bitfinex order submit response: [MTS, TYPE, MESSAGE_ID, null, [ORDER_ARRAY], ...]
            if (result.Count >= 5 && result[4] is JArray orderData)
            {
                if (orderData.Count > 0 && orderData[0] is JArray orderArr)
                    return ParseOrder(orderArr, symbol);
                return ParseOrder(orderData, symbol);
            }

            return new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = Guid.NewGuid().ToString(),
                Symbol = symbol,
                Status = ExchangeOrderStatus.New,
                Timestamp = DateTime.UtcNow
            };
        }

        private ExchangeOrder ParseOrder(JArray order, string symbol)
        {
            // Bitfinex order array format:
            // [ID, GID, CID, SYMBOL, MTS_CREATE, MTS_UPDATE, AMOUNT, AMOUNT_ORIG,
            //  TYPE, TYPE_PREV, MTS_TIF, _PLACEHOLDER, FLAGS, STATUS, _PLACEHOLDER,
            //  _PLACEHOLDER, PRICE, PRICE_AVG, PRICE_TRAILING, PRICE_AUX_LIMIT, ...]
            var amount = order.Count > 7 ? order[7]?.Value<decimal>() ?? 0m : 0m;
            var filledAmount = order.Count > 7 ? (order[7]?.Value<decimal>() ?? 0m) - (order[6]?.Value<decimal>() ?? 0m) : 0m;

            return new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = order[0]?.ToString() ?? "",
                ClientOrderId = order.Count > 2 ? order[2]?.ToString() : null,
                Symbol = symbol,
                Side = BitfinexMapper.MapOrderSideFromAmount(amount),
                Type = BitfinexMapper.MapOrderType(order.Count > 8 ? order[8]?.ToString() : null),
                Status = BitfinexMapper.MapOrderStatus(order.Count > 13 ? order[13]?.ToString() : null),
                Price = order.Count > 16 ? order[16]?.Value<decimal>() ?? 0m : 0m,
                Quantity = Math.Abs(amount),
                FilledQuantity = Math.Abs(filledAmount),
                Timestamp = order.Count > 4
                    ? BitfinexMapper.FromUnixMilliseconds(order[4]?.Value<long>() ?? 0)
                    : DateTime.UtcNow
            };
        }

        #endregion
    }
}
