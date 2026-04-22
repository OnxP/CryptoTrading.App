using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.TradeRequest;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoTrading.App.Broker
{
    /// <summary>
    /// Pure in-memory IMarket used by backtests. Returns synthetic neutral
    /// <see cref="ExchangeOrder"/> fills — no exchange SDK involved.
    /// </summary>
    public class TestMarket : IMarket
    {
        List<IRequest> trades = new List<IRequest>();
        public Task<IEnumerable<ExchangeBalance>> GetAccountBalances()
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<ExchangeOrder>> GetAllOpenOrders()
        {
            throw new NotImplementedException();
        }

        public Task<ExchangeOrder> SetMarketOrder(IMarketRequest trade)
        {
            trades.Add(trade);
            var side = trade.OrderType ?? ExchangeOrderSide.Buy;
            var order = ExchangeOrder.CreateFilledOrder(
                exchangeId: "Test",
                symbol: trade.Symbol,
                side: side,
                price: trade.Price,
                quantity: trade.Quantity,
                timestamp: DateTime.Now);
            order.Type = ExchangeOrderType.Market;
            return Task.FromResult(order);
        }

        public Task<string> CancelOrder(ICancelRequest request)
        {
            return Task.FromResult(string.Empty);
        }

        public Task<ExchangeOrder> SetStopLimitOrder(IStopLimitRequest trade)
        {
            trades.Add(trade);
            var side = trade.OrderType ?? ExchangeOrderSide.Sell;
            var order = new ExchangeOrder
            {
                ExchangeId = "Test",
                OrderId = Guid.NewGuid().ToString(),
                Symbol = trade.Symbol,
                Side = side,
                Type = ExchangeOrderType.StopLimit,
                Status = ExchangeOrderStatus.New,
                Price = trade.StopPrice,
                StopPrice = trade.StopPrice,
                Quantity = trade.Quantity,
                FilledQuantity = trade.Quantity,
                QuoteQuantity = trade.Quantity * trade.StopPrice,
                Timestamp = DateTime.Now
            };
            return Task.FromResult(order);
        }

        public Task<ExchangeOrder> SetLimitOrder(ILimitRequest trade)
        {
            trades.Add(trade);
            var side = trade.OrderType ?? ExchangeOrderSide.Buy;
            var order = ExchangeOrder.CreateFilledOrder(
                exchangeId: "Test",
                symbol: trade.Symbol,
                side: side,
                price: trade.Price,
                quantity: trade.Quantity,
                timestamp: DateTime.Now);
            order.Type = ExchangeOrderType.Limit;
            return Task.FromResult(order);
        }
    }
}
