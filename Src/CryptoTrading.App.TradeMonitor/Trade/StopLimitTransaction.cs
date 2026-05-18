namespace CryptoTrading.App.Monitor.Trade
{
    public class StopLimitTransaction : Transaction
    {
        public override TransactionType Type => TransactionType.StopLimitTransaction;
    }
}
