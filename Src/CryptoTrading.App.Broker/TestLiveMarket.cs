using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.TradeRequest;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Broker
{
    public class TestLiveMarket : IMarket
    {
        private readonly IExchangeProvider _provider;
        private readonly ILogger<TestLiveMarket> _logger;
        public TestLiveMarket(ILogger<TestLiveMarket> logger, IExchangeProvider provider)
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
            return await _provider.PlaceMarketOrderAsync(
                trade.Symbol,
                trade.OrderType ?? ExchangeOrderSide.Buy,
                trade.Quantity);
        }

        public async Task<ExchangeOrder> SetLimitOrder(IStopLimitRequest trade)
        {
            return await _provider.PlaceLimitOrderAsync(
                trade.Symbol,
                trade.OrderType ?? ExchangeOrderSide.Buy,
                trade.StopPrice,
                trade.Quantity);
        }

        private void LogOrder(string symbol, string order, ExchangeOrderStatus status)
        {
            _logger.LogInformation($"{symbol} - {order} {status.ToString()}");
        }

        public async Task<string> CancelOrder(ICancelRequest order)
        {
            LogOrder(order.Symbol, order.ClientOrderId.ToString(), ExchangeOrderStatus.Cancelled);
            return await Task.Run(() => "");
        }
    }
}
