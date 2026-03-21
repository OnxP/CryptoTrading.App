namespace CryptoTrading.App.Core.TradeRequest
{
    public interface ICancelRequest : IRequest
    {
        string ClientOrderId { get; set; }
    }

    public interface ICancelTradeRequest : IRequest
    {
        string ClientOrderId { get; set; }
    }
}
