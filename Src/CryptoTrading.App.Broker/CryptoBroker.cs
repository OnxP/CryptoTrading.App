using System.Threading.Tasks;
using CryptoTrading.App.Broker.Account;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Broker
{
    public class CryptoBroker : IBroker
    {
        private readonly IMarket _market;
        private readonly IAccount _account;
        private readonly ILogger<CryptoBroker> _logger;
        private string KeyValue { get; set; }

        public CryptoBroker(IMarket market, IAccount account, ILogger<CryptoBroker> logger)
        {
            _market = market;
            _account = account;
            _logger = logger;
            KeyValue = string.IsNullOrEmpty(KeyValue) ? "1" : KeyValue;
        }

        public CryptoBroker(IMarket market, IAccount account, ILogger<CryptoBroker> logger, IKey key)
            : this(market, account, logger)
        {
            KeyValue = key.KeyValue;
        }

        public IAccount Account => _account;

        public async Task<IBrokerResult> SubmitTradeRequest(ITradeRequest request)
        {
            if (!_account.HasSufficientBalance(request))
            {
                _logger.LogWarning("Trade rejected for {Symbol}: insufficient balance", request.Symbol);
                return OrderResult.Rejected($"Insufficient balance for {request.Symbol}");
            }

            if (_account.Positions.GetPosition(request.BaseSymbol).HasOpenPosition)
            {
                _logger.LogWarning("Trade rejected for {Symbol}: position already open", request.Symbol);
                return OrderResult.Rejected($"Position already open for {request.BaseSymbol}");
            }

            var side = request.OrderSide;
            var order = await _market.SetMarketOrder(new BrokerMarketRequest
            {
                Symbol = request.Symbol,
                OrderType = side,
                Quantity = CalculateQuantity(request),
                Price = 0m
            }).ConfigureAwait(false);

            if (order.IsFilled)
            {
                _account.UpdatePositionFromFill(order, request);
                _logger.LogInformation("Trade filled for {Symbol}: {Qty} @ {Price}", order.Symbol, order.FilledQuantity, order.Price);
            }

            return OrderResult.Success(order);
        }

        private decimal CalculateQuantity(ITradeRequest request)
        {
            if (request.Leverage > 1)
                return request.Amount;
            return request.Amount;
        }

        public async Task<ExchangeOrder> SubmitMarketOrder(string symbol, ExchangeOrderSide side, decimal quantity)
        {
            var request = new BrokerMarketRequest
            {
                Symbol = symbol,
                OrderType = side,
                Quantity = quantity,
                Price = 0m
            };
            var order = await _market.SetMarketOrder(request).ConfigureAwait(false);
            _logger.LogInformation("Order {OrderId} {Symbol} {Status}", order.OrderId, order.Symbol, order.Status);
            return order;
        }

        public async Task<ExchangeOrder> SubmitLimitOrder(string symbol, ExchangeOrderSide side, decimal quantity, decimal price)
        {
            var request = new BrokerLimitRequest
            {
                Symbol = symbol,
                OrderType = side,
                Quantity = quantity,
                Price = price
            };
            var order = await _market.SetLimitOrder(request).ConfigureAwait(false);
            _logger.LogInformation("Order {OrderId} {Symbol} {Status}", order.OrderId, order.Symbol, order.Status);
            return order;
        }

        public async Task<ExchangeOrder> CancelOrder(string symbol, string orderId)
        {
            var request = new CancelRequest(orderId, symbol);
            var cancelledId = await _market.CancelOrder(request).ConfigureAwait(false);
            _logger.LogInformation("Order {OrderId} cancelled for {Symbol}", orderId, symbol);
            return new ExchangeOrder
            {
                OrderId = orderId,
                Symbol = symbol,
                Status = ExchangeOrderStatus.Cancelled
            };
        }

        private void LogOrder(ExchangeOrder order, ExchangeOrderStatus status)
        {
            _logger.LogInformation("Order {OrderId} {Symbol} {Status}", order.OrderId, order.Symbol, order.Status);
        }
    }
}
