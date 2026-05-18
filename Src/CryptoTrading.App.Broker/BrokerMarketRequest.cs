using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.TradeRequest;

namespace CryptoTrading.App.Broker
{
    internal class BrokerMarketRequest : IMarketRequest
    {
        public string Symbol { get; set; }
        public ExchangeOrderSide? OrderType { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
    }

    internal class BrokerLimitRequest : ILimitRequest
    {
        public string Symbol { get; set; }
        public ExchangeOrderSide? OrderType { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
    }

    internal class BrokerStopLimitRequest : IStopLimitRequest
    {
        public string Symbol { get; set; }
        public ExchangeOrderSide? OrderType { get; set; }
        public decimal Quantity { get; set; }
        public decimal StopPrice { get; set; }
    }
}
