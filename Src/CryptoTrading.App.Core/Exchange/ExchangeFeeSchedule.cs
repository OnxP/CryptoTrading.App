namespace CryptoTrading.App.Core.Exchange
{
    public class ExchangeFeeSchedule
    {
        public string ExchangeId { get; set; }
        public decimal MakerFeeRate { get; set; }
        public decimal TakerFeeRate { get; set; }
        public string FeeAsset { get; set; }
        public decimal FeeDiscount { get; set; }
        public bool HasFeeDiscount { get; set; }
        public decimal DiscountRate { get; set; }

        public ExchangeFeeSchedule() { }

        public ExchangeFeeSchedule(string exchangeId, decimal makerFeeRate, decimal takerFeeRate, string feeAsset)
        {
            ExchangeId = exchangeId;
            MakerFeeRate = makerFeeRate;
            TakerFeeRate = takerFeeRate;
            FeeAsset = feeAsset;
        }

        public decimal CalculateTakerFee(decimal orderValue)
        {
            var fee = orderValue * TakerFeeRate;
            if (HasFeeDiscount)
            {
                fee *= (1 - DiscountRate);
            }
            return fee;
        }

        public decimal CalculateMakerFee(decimal orderValue)
        {
            var fee = orderValue * MakerFeeRate;
            if (HasFeeDiscount)
            {
                fee *= (1 - DiscountRate);
            }
            return fee;
        }
    }
}
