using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.TradeRequest;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Broker
{
    public class LiveMarket : IMarket
    {
        private readonly IExchangeProvider _provider;
        private readonly ILogger<LiveMarket> _logger;
        public LiveMarket(ILogger<LiveMarket> logger, IExchangeProvider provider)
        {
            _provider = provider;
            _logger = logger;
        }
        public async Task<IEnumerable<ExchangeBalance>> GetAccountBalances()
        {
            var balances = await _provider.GetBalancesAsync();
            return balances.Where(x => x.Free > 0);
        }

        public Task<IEnumerable<ExchangeOrder>> GetAllOpenOrders()
        {
            throw new NotImplementedException();
        }

        public async Task<ExchangeOrder> SetMarketOrder(IMarketRequest trade)
        {
            var order = await _provider.PlaceMarketOrderAsync(
                trade.Symbol,
                trade.OrderType ?? ExchangeOrderSide.Buy,
                trade.Quantity);

            return order;
        }

        public async Task<ExchangeOrder> SetLimitOrder(IStopLimitRequest trade)
        {
            var order = await _provider.PlaceLimitOrderAsync(
                trade.Symbol,
                trade.OrderType ?? ExchangeOrderSide.Buy,
                trade.StopPrice,
                trade.Quantity);

            LogOrder(order.Symbol, order.ClientOrderId, ExchangeOrderStatus.Filled);

            return order;
        }

        private void LogOrder(string symbol, string order, ExchangeOrderStatus status)
        {
            _logger.LogInformation($"{symbol} - {order} {status.ToString()}");
        }

        public async Task<string> CancelOrder(ICancelRequest order)
        {
            LogOrder(order.Symbol, order.ClientOrderId.ToString(), ExchangeOrderStatus.Cancelled);
            var result = await _provider.CancelOrderAsync(order.Symbol, order.ClientOrderId.ToString());
            return result.OrderId;
        }
    }
}
