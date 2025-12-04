namespace CryptoTrading.App.Algorithm
{
    public interface IMarketStructureResult
    {
    }
    public class MarketStructureResult : IMarketStructureResult
    {
        public MarketRegime MarketRegime { get; internal set; }
    }
}