namespace CryptoTrading.App.Algorithm
{
    public interface IMarketStructureResult
    {
        public MarketRegime MarketRegime { get; }
    }
    public class MarketStructureResult : IMarketStructureResult
    {
        public MarketRegime MarketRegime { get; internal set; }
    }
}