namespace CryptoTrading.App.Core.Exchange
{
    public class ExchangeFeeSchedule
    {
        public string ExchangeId { get; set; }
        public decimal MakerFeeRate { get; set; }
        public decimal TakerFeeRate { get; set; }
        public string FeeAsset { get; set; }
        public decimal FeeDiscount { get; set; }
    }
}
