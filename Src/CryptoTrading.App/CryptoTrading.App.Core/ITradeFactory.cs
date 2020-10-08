namespace CryptoTrading.App.Core
{
    public interface ITradeFactory
    {
        ITrade CreateTrade(string requestBuySymbol, string requestSellSymbol);
    }
}