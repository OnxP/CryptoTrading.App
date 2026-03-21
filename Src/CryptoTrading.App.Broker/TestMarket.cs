using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.TradeRequest;

namespace CryptoTrading.App.Broker
{
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
            var order = ExchangeOrder.CreateFilledOrder(
                "test",
                trade.Symbol,
                ExchangeOrderSide.Buy,
                trade.Price,
                trade.Quantity,
                DateTime.Now);
            return Task.FromResult(order);
        }

        public Task<string> CancelOrder(ICancelRequest request)
        {
            return Task.Run(() => "");
        }

        public Task<ExchangeOrder> SetStopLimitOrder(IStopLimitRequest trade)
        {
            trades.Add(trade);
            var order = new ExchangeOrder
            {
                ExchangeId = "test",
                OrderId = Guid.NewGuid().ToString(),
                ClientOrderId = Guid.NewGuid().ToString(),
                Symbol = trade.Symbol,
                Side = ExchangeOrderSide.Sell,
                Type = ExchangeOrderType.StopLimit,
                Status = ExchangeOrderStatus.New,
                Price = trade.StopPrice,
                StopPrice = trade.StopPrice,
                Quantity = trade.Quantity,
                FilledQuantity = 0,
                QuoteQuantity = trade.Quantity * trade.StopPrice,
                Timestamp = DateTime.Now
            };
            return Task.FromResult(order);
        }

        public Task<ExchangeOrder> SetLimitOrder(ILimitRequest trade)
        {
            trades.Add(trade);
            var order = ExchangeOrder.CreateFilledOrder(
                "test",
                trade.Symbol,
                ExchangeOrderSide.Buy,
                trade.Price,
                trade.Quantity,
                DateTime.Now);
            return Task.FromResult(order);
        }
    }
}
