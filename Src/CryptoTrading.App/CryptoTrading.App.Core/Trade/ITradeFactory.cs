namespace CryptoTrading.App.Core.Trade
{
    public interface ITradeFactory
    {
        ITrade CreateTrade(string requestBuySymbol, string requestSellSymbol);
    }
}