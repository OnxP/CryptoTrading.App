namespace CryptoTrading.App.Monitor.Trade
{
    public class MarketTransaction : Transaction
    {
        public override TransactionType Type => TransactionType.MarketTransaction;
    }
}
