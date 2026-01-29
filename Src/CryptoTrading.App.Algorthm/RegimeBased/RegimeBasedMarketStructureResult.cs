using CryptoTrading.App.Core.Strategy;
using System.Collections.Generic;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// Extended market structure result with regime-based information.
    /// Implements IMarketStructureResult from Core while adding regime-specific data.
    /// </summary>
    public class RegimeBasedMarketStructureResult : IMarketStructureResult
    {
        // Required by IMarketStructureResult
        public MarketRegime MarketRegime { get; set; }

        // Extended properties for regime-based strategy
        public VolatilityRegime VolatilityRegime { get; set; }
        public AllowedDirection AllowedDirection { get; set; }
        public List<SetupType> AllowedSetups { get; set; } = new List<SetupType>();
        public decimal Confidence { get; set; }
        public string Reasoning { get; set; } = string.Empty;

        // Data passed to downstream strategies
        public decimal CurrentAtr { get; set; }
        public decimal CurrentRsi { get; set; }
        public decimal TrendStrength { get; set; }
    }
}