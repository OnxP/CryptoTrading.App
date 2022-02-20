namespace CryptoTrading.App.Core.Trade
{
    public class StopLimitTransaction : Transaction
    {
        public override TransactionType Type => TransactionType.StopLimitTransaction;
    }
}
