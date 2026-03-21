using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Interfaces.Clients;
using CryptoExchange.Net.Authentication;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Exchange.Binance
{
    public class BinanceExchangeProvider : IExchangeProvider, IDisposable
    {
        private readonly IBinanceRestClient _restClient;
        private readonly IBinanceSocketClient _socketClient;

        public string ExchangeId => "Binance";

        public BinanceExchangeProvider(string apiKey, string apiSecret)
        {
            _restClient = new BinanceRestClient(options =>
            {
                if (!string.IsNullOrEmpty(apiKey))
                    options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            });
            _socketClient = new BinanceSocketClient(options =>
            {
                if (!string.IsNullOrEmpty(apiKey))
                    options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            });
        }

        public async Task<IEnumerable<ExchangeBalance>> GetBalancesAsync()
        {
            var result = await _restClient.SpotApi.Account.GetAccountInfoAsync();
            if (!result.Success) throw new Exception($"Binance GetAccountInfo failed: {result.Error}");
            return result.Data.Balances.Select(b => new ExchangeBalance
            {
                ExchangeId = ExchangeId,
                Asset = b.Asset,
                Free = b.Available,
                Locked = b.Locked
            });
        }

        public async Task<IEnumerable<ExchangeSymbol>> GetSymbolsAsync()
        {
            var result = await _restClient.SpotApi.ExchangeData.GetExchangeInfoAsync();
            if (!result.Success) throw new Exception($"Binance GetExchangeInfo failed: {result.Error}");
            return result.Data.Symbols.Where(s => s.Status == SymbolStatus.Trading).Select(s => new ExchangeSymbol
            {
                ExchangeId = ExchangeId,
                Ticker = s.Name,
                BaseAsset = s.BaseAsset,
                QuoteAsset = s.QuoteAsset,
                TickSize = s.PriceFilter?.TickSize ?? 0.00000001m,
                StepSize = s.LotSizeFilter?.StepSize ?? 0.00000001m,
                MinQuantity = s.LotSizeFilter?.MinQuantity ?? 0,
                MaxQuantity = s.LotSizeFilter?.MaxQuantity ?? 0,
                MinNotional = s.MinNotionalFilter?.MinNotional ?? 0,
                IsActive = true
            });
        }

        public Task<ExchangeFeeSchedule> GetFeeScheduleAsync()
        {
            return Task.FromResult(new ExchangeFeeSchedule
            {
                ExchangeId = ExchangeId,
                MakerFeeRate = 0.001m,
                TakerFeeRate = 0.001m,
                FeeAsset = "BNB",
                FeeDiscount = 0.25m
            });
        }

        public async Task<ExchangeOrder> PlaceMarketOrderAsync(string symbol, ExchangeOrderSide side, decimal quantity)
        {
            var result = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                symbol,
                BinanceMapper.MapToBinanceOrderSide(side),
                SpotOrderType.Market,
                quantity: quantity);
            if (!result.Success) throw new Exception($"Binance PlaceOrder failed: {result.Error}");
            return new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = result.Data.Id.ToString(),
                ClientOrderId = result.Data.ClientOrderId,
                Symbol = result.Data.Symbol,
                Side = BinanceMapper.MapOrderSide(result.Data.Side),
                Type = ExchangeOrderType.Market,
                Status = BinanceMapper.MapOrderStatus(result.Data.Status),
                Price = result.Data.Price,
                Quantity = result.Data.Quantity,
                FilledQuantity = result.Data.QuantityFilled,
                QuoteQuantity = result.Data.QuoteQuantityFilled,
                Timestamp = result.Data.CreateTime
            };
        }

        public async Task<ExchangeOrder> PlaceLimitOrderAsync(string symbol, ExchangeOrderSide side, decimal price, decimal quantity)
        {
            var result = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                symbol,
                BinanceMapper.MapToBinanceOrderSide(side),
                SpotOrderType.Limit,
                quantity: quantity,
                price: price,
                timeInForce: TimeInForce.GoodTillCanceled);
            if (!result.Success) throw new Exception($"Binance PlaceOrder failed: {result.Error}");
            return new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = result.Data.Id.ToString(),
                ClientOrderId = result.Data.ClientOrderId,
                Symbol = result.Data.Symbol,
                Side = BinanceMapper.MapOrderSide(result.Data.Side),
                Type = ExchangeOrderType.Limit,
                Status = BinanceMapper.MapOrderStatus(result.Data.Status),
                Price = result.Data.Price,
                Quantity = result.Data.Quantity,
                FilledQuantity = result.Data.QuantityFilled,
                QuoteQuantity = result.Data.QuoteQuantityFilled,
                Timestamp = result.Data.CreateTime
            };
        }

        public async Task<ExchangeOrder> PlaceStopLimitOrderAsync(string symbol, ExchangeOrderSide side, decimal stopPrice, decimal limitPrice, decimal quantity)
        {
            var result = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                symbol,
                BinanceMapper.MapToBinanceOrderSide(side),
                SpotOrderType.StopLossLimit,
                quantity: quantity,
                price: limitPrice,
                stopPrice: stopPrice,
                timeInForce: TimeInForce.GoodTillCanceled);
            if (!result.Success) throw new Exception($"Binance PlaceOrder failed: {result.Error}");
            return new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = result.Data.Id.ToString(),
                ClientOrderId = result.Data.ClientOrderId,
                Symbol = result.Data.Symbol,
                Side = BinanceMapper.MapOrderSide(result.Data.Side),
                Type = ExchangeOrderType.StopLimit,
                Status = BinanceMapper.MapOrderStatus(result.Data.Status),
                Price = result.Data.Price,
                StopPrice = stopPrice,
                Quantity = result.Data.Quantity,
                FilledQuantity = result.Data.QuantityFilled,
                QuoteQuantity = result.Data.QuoteQuantityFilled,
                Timestamp = result.Data.CreateTime
            };
        }

        public async Task<ExchangeOrder> GetOrderAsync(string symbol, string orderId)
        {
            var result = await _restClient.SpotApi.Trading.GetOrderAsync(symbol, long.Parse(orderId));
            if (!result.Success) throw new Exception($"Binance GetOrder failed: {result.Error}");
            return new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = result.Data.Id.ToString(),
                ClientOrderId = result.Data.ClientOrderId,
                Symbol = result.Data.Symbol,
                Side = BinanceMapper.MapOrderSide(result.Data.Side),
                Type = BinanceMapper.MapOrderType(result.Data.Type),
                Status = BinanceMapper.MapOrderStatus(result.Data.Status),
                Price = result.Data.Price,
                StopPrice = result.Data.StopPrice ?? 0,
                Quantity = result.Data.Quantity,
                FilledQuantity = result.Data.QuantityFilled,
                QuoteQuantity = result.Data.QuoteQuantityFilled,
                Timestamp = result.Data.CreateTime
            };
        }

        public async Task<ExchangeOrder> CancelOrderAsync(string symbol, string orderId)
        {
            var result = await _restClient.SpotApi.Trading.CancelOrderAsync(symbol, long.Parse(orderId));
            if (!result.Success) throw new Exception($"Binance CancelOrder failed: {result.Error}");
            return new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = result.Data.Id.ToString(),
                ClientOrderId = result.Data.ClientOrderId,
                Symbol = result.Data.Symbol,
                Status = BinanceMapper.MapOrderStatus(result.Data.Status)
            };
        }

        public async Task<IEnumerable<ExchangeOrder>> GetOpenOrdersAsync()
        {
            var result = await _restClient.SpotApi.Trading.GetOpenOrdersAsync();
            if (!result.Success) throw new Exception($"Binance GetOpenOrders failed: {result.Error}");
            return result.Data.Select(o => new ExchangeOrder
            {
                ExchangeId = ExchangeId,
                OrderId = o.Id.ToString(),
                ClientOrderId = o.ClientOrderId,
                Symbol = o.Symbol,
                Side = BinanceMapper.MapOrderSide(o.Side),
                Type = BinanceMapper.MapOrderType(o.Type),
                Status = BinanceMapper.MapOrderStatus(o.Status),
                Price = o.Price,
                StopPrice = o.StopPrice ?? 0,
                Quantity = o.Quantity,
                FilledQuantity = o.QuantityFilled,
                QuoteQuantity = o.QuoteQuantityFilled,
                Timestamp = o.CreateTime
            });
        }

        public async Task<IEnumerable<ExchangeCandlestick>> GetCandlesticksAsync(string symbol, CandleInterval interval, DateTime from, DateTime to)
        {
            var result = await _restClient.SpotApi.ExchangeData.GetKlinesAsync(
                symbol,
                BinanceMapper.MapToBinanceCandleInterval(interval),
                from,
                to,
                limit: 1000);
            if (!result.Success) throw new Exception($"Binance GetKlines failed: {result.Error}");
            return result.Data.Select(k => new ExchangeCandlestick
            {
                ExchangeId = ExchangeId,
                Symbol = symbol,
                Interval = interval,
                OpenTime = k.OpenTime,
                CloseTime = k.CloseTime,
                Open = k.OpenPrice,
                High = k.HighPrice,
                Low = k.LowPrice,
                Close = k.ClosePrice,
                Volume = k.Volume,
                QuoteVolume = k.QuoteVolume,
                NumberOfTrades = k.TradeCount,
                TakerBuyBaseAssetVolume = k.TakerBuyBaseVolume,
                TakerBuyQuoteAssetVolume = k.TakerBuyQuoteVolume
            });
        }

        public async Task SubscribeCandlestickAsync(string symbol, CandleInterval interval, Action<ExchangeCandlestick> onCandle)
        {
            await _socketClient.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(
                symbol,
                BinanceMapper.MapToBinanceCandleInterval(interval),
                data =>
                {
                    onCandle(new ExchangeCandlestick
                    {
                        ExchangeId = ExchangeId,
                        Symbol = data.Data.Symbol,
                        Interval = interval,
                        OpenTime = data.Data.Data.OpenTime,
                        CloseTime = data.Data.Data.CloseTime,
                        Open = data.Data.Data.OpenPrice,
                        High = data.Data.Data.HighPrice,
                        Low = data.Data.Data.LowPrice,
                        Close = data.Data.Data.ClosePrice,
                        Volume = data.Data.Data.Volume,
                        QuoteVolume = data.Data.Data.QuoteVolume,
                        NumberOfTrades = data.Data.Data.TradeCount,
                        TakerBuyBaseAssetVolume = data.Data.Data.TakerBuyBaseVolume,
                        TakerBuyQuoteAssetVolume = data.Data.Data.TakerBuyQuoteVolume
                    });
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
