namespace CryptoTrading.App.Broker.Position
{
    public class TransactionLeg
    {
        public string Symbol { get; set; }
        public decimal Quantity { get; set; }
        public TransactionLegStatus Status { get; set; } = TransactionLegStatus.Pending;
    }

    public enum TransactionLegStatus { Pending, Completed, Cancelled }
}
