namespace CryptoTrading.App.Core.TradeRequest
{
    public interface ICancelRequest : IRequest
    {
        long ClientOrderId { get; set; }
    }
}
