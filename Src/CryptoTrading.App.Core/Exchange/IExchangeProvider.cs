using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.Exchange
{
    public interface IExchangeProvider
    {
        string ExchangeId { get; }

        // Account
        Task<IEnumerable<ExchangeBalance>> GetBalancesAsync();
        Task<IEnumerable<ExchangeSymbol>> GetSymbolsAsync();
        Task<ExchangeFeeSchedule> GetFeeScheduleAsync();

        // Orders
        Task<ExchangeOrder> PlaceMarketOrderAsync(string symbol, ExchangeOrderSide side, decimal quantity);
        Task<ExchangeOrder> PlaceLimitOrderAsync(string symbol, ExchangeOrderSide side, decimal price, decimal quantity);
        Task<ExchangeOrder> PlaceStopLimitOrderAsync(string symbol, ExchangeOrderSide side, decimal stopPrice, decimal limitPrice, decimal quantity);
        Task<ExchangeOrder> GetOrderAsync(string symbol, string orderId);
        Task<ExchangeOrder> CancelOrderAsync(string symbol, string orderId);
        Task<IEnumerable<ExchangeOrder>> GetOpenOrdersAsync();

        // Market Data (REST)
        Task<IEnumerable<ExchangeCandlestick>> GetCandlesticksAsync(string symbol, CandleInterval interval, DateTime from, DateTime to);

        // Market Data (WebSocket)
        Task SubscribeCandlestickAsync(string symbol, CandleInterval interval, Action<ExchangeCandlestick> onCandle);
        Task UnsubscribeAllAsync();
    }
}
