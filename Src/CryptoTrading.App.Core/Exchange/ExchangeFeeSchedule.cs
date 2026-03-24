namespace CryptoTrading.App.Core.Exchange
{
    /// <summary>
    /// Fee schedule for a specific exchange.
    /// Allows per-exchange fee calculation instead of hardcoded rates.
    /// </summary>
    public class ExchangeFeeSchedule
    {
        public string ExchangeId { get; set; }
        public decimal MakerFee { get; set; }    // e.g. 0.001 for 0.1%
        public decimal TakerFee { get; set; }    // e.g. 0.001 for 0.1%
        public string FeeAsset { get; set; }     // e.g. "USDT", "BNB"
        public bool HasFeeDiscount { get; set; }
        public decimal DiscountRate { get; set; } // e.g. 0.25 for 25% discount

        public ExchangeFeeSchedule() { }

        public ExchangeFeeSchedule(string exchangeId, decimal makerFee, decimal takerFee, string feeAsset = "USDT")
        {
            ExchangeId = exchangeId;
            MakerFee = makerFee;
            TakerFee = takerFee;
            FeeAsset = feeAsset;
        }

        /// <summary>
        /// Calculate the fee for a given order value using taker rate
        /// </summary>
        public decimal CalculateTakerFee(decimal orderValue)
        {
            var rate = HasFeeDiscount ? TakerFee * (1 - DiscountRate) : TakerFee;
            return orderValue * rate;
        }

        /// <summary>
        /// Calculate the fee for a given order value using maker rate
        /// </summary>
        public decimal CalculateMakerFee(decimal orderValue)
        {
            var rate = HasFeeDiscount ? MakerFee * (1 - DiscountRate) : MakerFee;
            return orderValue * rate;
        }
    }
}
