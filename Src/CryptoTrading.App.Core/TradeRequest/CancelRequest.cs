namespace CryptoTrading.App.Core.TradeRequest
{
    public class CancelRequest : ICancelRequest
    {
        public CancelRequest(string clientOrderId, string symbol)
        {
            Symbol = symbol;
            ClientOrderId = clientOrderId;
        }

        public string ClientOrderId { get; set; }

        public string Symbol { get; set; }
    }
}
