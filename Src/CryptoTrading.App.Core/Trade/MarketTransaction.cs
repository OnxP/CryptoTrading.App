namespace CryptoTrading.App.Core.Trade
{
    public class MarketTransaction : Transaction
    {
        public override TransactionType Type => TransactionType.MarketTransaction;
    }
}
