using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bitfinex.Net.Clients;
using Bitfinex.Net.Enums;
using Bitfinex.Net.Interfaces.Clients;
using CryptoExchange.Net.Authentication;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Exchange.Bitfinex
{
    public class BitfinexExchangeProvider : IExchangeProvider, IDisposable
    {
        private readonly IBitfinexRestClient _restClient;
        private readonly IBitfinexSocketClient _socketClient;

        public string ExchangeId => "Bitfinex";

        public BitfinexExchangeProvider(string apiKey, string apiSecret)
        {
            _restClient = new BitfinexRestClient(options =>
            {
                if (!string.IsNullOrEmpty(apiKey))
                    options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            });
            _socketClient = new BitfinexSocketClient(options =>
            {
                if (!string.IsNullOrEmpty(apiKey))
                    options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            });
        }

        public async Task<IEnumerable<ExchangeBalance>> GetBalancesAsync()
        {
            var result = await _restClient.SpotApi.Account.GetBalancesAsync();
            if (!result.Success) throw new Exception($"Bitfinex GetBalances failed: {result.Error}");
            return result.Data.Select(b => new ExchangeBalance
            {
                ExchangeId = ExchangeId,
                Asset = b.Asset,
                Free = b.Available ?? 0,
                Locked = b.Total - (b.Available ?? 0)
            });
        }

        public async Task<IEnumerable<ExchangeSymbol>> GetSymbolsAsync()
        {
            var result = await _restClient.SpotApi.ExchangeData.GetSymbolsAsync();
            if (!result.Success) throw new Exception($"Bitfinex GetSymbols failed: {result.Error}");
            return result.Data.Select(s => new ExchangeSymbol
            {
                ExchangeId = ExchangeId,
                Ticker = BitfinexMapper.ToStandardSymbol(s.Key),
                BaseAsset = BitfinexMapper.ToStandardSymbol(s.Key).Length >= 6
                    ? BitfinexMapper.ToStandardSymbol(s.Key).Substring(0, BitfinexMapper.ToStandardSymbol(s.Key).Length - 4)
                    : BitfinexMapper.ToStandardSymbol(s.Key).Substring(0, 3),
                QuoteAsset = BitfinexMapper.ToStandardSymbol(s.Key).Length >= 6
                    ? BitfinexMapper.ToStandardSymbol(s.Key).Substring(BitfinexMapper.ToStandardSymbol(s.Key).Length - 4)
                    : BitfinexMapper.ToStandardSymbol(s.Key).Substring(3),
                TickSize = 0.00000001m,
                StepSize = 0.00000001m,
                MinQuantity = 0,
                MaxQuantity = 0,
                MinNotional = 0,
                IsActive = true
            });
        }

        public Task<ExchangeFeeSchedule> GetFeeScheduleAsync()
        {
            return Task.FromResult(new ExchangeFeeSchedule
            {
                ExchangeId = ExchangeId,
                MakerFeeRate = 0.001m,
                TakerFeeRate = 0.002m,
                FeeAsset = "USD",
                FeeDiscount = 0
            });
        }

        public async Task<ExchangeOrder> PlaceMarketOrderAsync(string symbol, ExchangeOrderSide side, decimal quantity)
        {
            var bfxSymbol = BitfinexMapper.ToBitfinexSymbol(symbol);
            var signedQty = side == ExchangeOrderSide.Buy ? quantity : -quantity;
            var result = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                bfxSymbol,
                BitfinexMapper.MapToBitfinexOrderSide(side),
                global::Bitfinex.Net.Enums.OrderType.ExchangeMarket,
                0,
                signedQty);
            if (!result.Success) throw new Exception($"Bitfinex PlaceOrder failed: {result.Error}");
            var order = result.Data.Data;
            return new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = order.Id.ToString(),
                Symbol = symbol,
                Side = side,
                Type = ExchangeOrderType.Market,
                Status = BitfinexMapper.MapOrderStatus(order.Status),
                Price = order.PriceAverage ?? order.Price,
                Quantity = Math.Abs(order.Quantity),
                FilledQuantity = Math.Abs(order.Quantity - order.QuantityRemaining),
                Timestamp = order.CreateTime
            };
        }

        public async Task<ExchangeOrder> PlaceLimitOrderAsync(string symbol, ExchangeOrderSide side, decimal price, decimal quantity)
        {
            var bfxSymbol = BitfinexMapper.ToBitfinexSymbol(symbol);
            var signedQty = side == ExchangeOrderSide.Buy ? quantity : -quantity;
            var result = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                bfxSymbol,
                BitfinexMapper.MapToBitfinexOrderSide(side),
                global::Bitfinex.Net.Enums.OrderType.ExchangeLimit,
                price,
                signedQty);
            if (!result.Success) throw new Exception($"Bitfinex PlaceOrder failed: {result.Error}");
            var order = result.Data.Data;
            return new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = order.Id.ToString(),
                Symbol = symbol,
                Side = side,
                Type = ExchangeOrderType.Limit,
                Status = BitfinexMapper.MapOrderStatus(order.Status),
                Price = order.Price,
                Quantity = Math.Abs(order.Quantity),
                FilledQuantity = Math.Abs(order.Quantity - order.QuantityRemaining),
                Timestamp = order.CreateTime
            };
        }

        public async Task<ExchangeOrder> PlaceStopLimitOrderAsync(string symbol, ExchangeOrderSide side, decimal stopPrice, decimal limitPrice, decimal quantity)
        {
            var bfxSymbol = BitfinexMapper.ToBitfinexSymbol(symbol);
            var signedQty = side == ExchangeOrderSide.Buy ? quantity : -quantity;
            var result = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                bfxSymbol,
                BitfinexMapper.MapToBitfinexOrderSide(side),
                global::Bitfinex.Net.Enums.OrderType.ExchangeStopLimit,
                limitPrice,
                signedQty,
                priceAuxLimit: stopPrice);
            if (!result.Success) throw new Exception($"Bitfinex PlaceOrder failed: {result.Error}");
            var order = result.Data.Data;
            return new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = order.Id.ToString(),
                Symbol = symbol,
                Side = side,
                Type = ExchangeOrderType.StopLimit,
                Status = BitfinexMapper.MapOrderStatus(order.Status),
                Price = order.Price,
                StopPrice = stopPrice,
                Quantity = Math.Abs(order.Quantity),
                FilledQuantity = Math.Abs(order.Quantity - order.QuantityRemaining),
                Timestamp = order.CreateTime
            };
        }

        public async Task<ExchangeOrder> GetOrderAsync(string symbol, string orderId)
        {
            var bfxSymbol = BitfinexMapper.ToBitfinexSymbol(symbol);
            var id = long.Parse(orderId);

            // Try open orders first
            var openResult = await _restClient.SpotApi.Trading.GetOpenOrdersAsync();
            if (openResult.Success)
            {
                var order = openResult.Data.FirstOrDefault(o => o.Id == id);
                if (order != null)
                {
                    return new ExchangeOrder
                    {
                        ExchangeId = ExchangeId,
                        OrderId = order.Id.ToString(),
                        Symbol = symbol,
                        Side = order.Quantity > 0 ? ExchangeOrderSide.Buy : ExchangeOrderSide.Sell,
                        Status = BitfinexMapper.MapOrderStatus(order.Status),
                        Price = order.Price,
                        Quantity = Math.Abs(order.Quantity),
                        FilledQuantity = Math.Abs(order.Quantity - order.QuantityRemaining),
                        Timestamp = order.CreateTime
                    };
                }
            }

            // Fall back to closed/historical orders
            var closedResult = await _restClient.SpotApi.Trading.GetClosedOrdersAsync(bfxSymbol);
            if (!closedResult.Success) throw new Exception($"Bitfinex GetOrder failed: {closedResult.Error}");
            var closedOrder = closedResult.Data.FirstOrDefault(o => o.Id == id);
            if (closedOrder == null) throw new Exception($"Bitfinex order {orderId} not found");
            return new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = closedOrder.Id.ToString(),
                Symbol = symbol,
                Side = closedOrder.Quantity > 0 ? ExchangeOrderSide.Buy : ExchangeOrderSide.Sell,
                Status = BitfinexMapper.MapOrderStatus(closedOrder.Status),
                Price = closedOrder.Price,
                Quantity = Math.Abs(closedOrder.Quantity),
                FilledQuantity = Math.Abs(closedOrder.Quantity - closedOrder.QuantityRemaining),
                Timestamp = closedOrder.CreateTime
            };
        }

        public async Task<ExchangeOrder> CancelOrderAsync(string symbol, string orderId)
        {
            var result = await _restClient.SpotApi.Trading.CancelOrderAsync(long.Parse(orderId));
            if (!result.Success) throw new Exception($"Bitfinex CancelOrder failed: {result.Error}");
            return new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = orderId,
                Symbol = symbol,
                Status = ExchangeOrderStatus.Cancelled
            };
        }

        public async Task<IEnumerable<ExchangeOrder>> GetOpenOrdersAsync()
        {
            var result = await _restClient.SpotApi.Trading.GetOpenOrdersAsync();
            if (!result.Success) throw new Exception($"Bitfinex GetOpenOrders failed: {result.Error}");
            return result.Data.Select(o => new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = o.Id.ToString(),
                Symbol = BitfinexMapper.ToStandardSymbol(o.Symbol),
                Side = o.Quantity > 0 ? ExchangeOrderSide.Buy : ExchangeOrderSide.Sell,
                Status = BitfinexMapper.MapOrderStatus(o.Status),
                Price = o.Price,
                Quantity = Math.Abs(o.Quantity),
                FilledQuantity = Math.Abs(o.Quantity - o.QuantityRemaining),
                Timestamp = o.CreateTime
            });
        }

        public async Task<IEnumerable<ExchangeCandlestick>> GetCandlesticksAsync(string symbol, CandleInterval interval, DateTime from, DateTime to)
        {
            var bfxSymbol = BitfinexMapper.ToBitfinexSymbol(symbol);
            var result = await _restClient.SpotApi.ExchangeData.GetKlinesAsync(
                bfxSymbol,
                BitfinexMapper.MapToBitfinexCandleInterval(interval),
                startTime: from,
                endTime: to,
                limit: 1000);
            if (!result.Success) throw new Exception($"Bitfinex GetKlines failed: {result.Error}");
            return result.Data.Select(k => new ExchangeCandlestick
            {
                ExchangeId = ExchangeId,
                Symbol = symbol,
                Interval = interval,
                OpenTime = k.OpenTime,
                CloseTime = BitfinexMapper.ComputeCloseTime(k.OpenTime, interval),
                Open = k.OpenPrice,
                High = k.HighPrice,
                Low = k.LowPrice,
                Close = k.ClosePrice,
                Volume = k.Volume
            });
        }

        public async Task SubscribeCandlestickAsync(string symbol, CandleInterval interval, Action<ExchangeCandlestick> onCandle)
        {
            var bfxSymbol = BitfinexMapper.ToBitfinexSymbol(symbol);
            await _socketClient.SpotApi.SubscribeToKlineUpdatesAsync(
                bfxSymbol,
                BitfinexMapper.MapToBitfinexCandleInterval(interval),
                data =>
                {
                    foreach (var kline in data.Data)
                    {
                        onCandle(new ExchangeCandlestick
                        {
                            ExchangeId = ExchangeId,
                            Symbol = symbol,
                            Interval = interval,
                            OpenTime = kline.OpenTime,
                            CloseTime = BitfinexMapper.ComputeCloseTime(kline.OpenTime, interval),
                            Open = kline.OpenPrice,
                            High = kline.HighPrice,
                            Low = kline.LowPrice,
                            Close = kline.ClosePrice,
                            Volume = kline.Volume
                        });
                    }
                });
        }

        public async Task UnsubscribeAllAsync()
        {
            await _socketClient.UnsubscribeAllAsync();
        }

        public void Dispose()
        {
            _restClient?.Dispose();
            _socketClient?.Dispose();
        }
    }
}
