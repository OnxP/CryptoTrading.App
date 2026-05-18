using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Broker.Account
{
    public class OrderResult : IBrokerResult
    {
        public bool Accepted { get; }
        public string RejectionReason { get; }
        public ExchangeOrder Order { get; }

        private OrderResult(bool accepted, ExchangeOrder order, string rejectionReason)
        {
            Accepted = accepted;
            Order = order;
            RejectionReason = rejectionReason;
        }

        public static OrderResult Success(ExchangeOrder order) => new(true, order, null);
        public static OrderResult Rejected(string reason) => new(false, null, reason);
    }
}
