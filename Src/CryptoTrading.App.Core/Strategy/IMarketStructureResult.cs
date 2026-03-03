namespace CryptoTrading.App.Core.Strategy
{
    public interface IMarketStructureResult
    {
        public MarketRegime MarketRegime { get; }
    }
    public class MarketStructureResult : IMarketStructureResult
    {
        public MarketRegime MarketRegime { get; set; }
    }
}