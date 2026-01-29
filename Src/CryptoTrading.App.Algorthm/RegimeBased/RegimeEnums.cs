namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// Volatility classification based on ATR percentile
    /// </summary>
    public enum VolatilityRegime
    {
        Low,    // ATR below 25th percentile - compression, potential breakout
        Normal, // ATR between 25th-75th percentile
        High    // ATR above 75th percentile - extended moves, wider stops
    }

    /// <summary>
    /// Trade direction filter from regime
    /// </summary>
    public enum AllowedDirection
    {
        Long,   // Only long setups allowed
        Short,  // Only short setups allowed
        Both,   // Both directions allowed (ranging market)
        None    // No trades allowed (unclear or dangerous conditions)
    }

    /// <summary>
    /// Types of setups the strategy can detect
    /// </summary>
    public enum SetupType
    {
        None,
        TrendContinuationLong,   // Pullback in uptrend
        TrendContinuationShort,  // Rally in downtrend
        MeanReversionLong,       // Oversold bounce at support
        MeanReversionShort,      // Overbought rejection at resistance
        BreakoutLong,            // Break above consolidation
        BreakoutShort,           // Break below consolidation
        FadeExtremeLong,         // Extreme oversold in uptrend (buy the dip)
        FadeExtremeShort         // Extreme overbought in downtrend (sell the rally)
    }

    /// <summary>
    /// Entry timing strategies for 1M execution
    /// </summary>
    public enum EntryStrategyType
    {
        None,
        LimitAtSupport,      // Place limit at support level, wait for fill
        LimitAtResistance,   // Place limit at resistance level, wait for fill
        MarketOnConfirmation, // Market order after N confirming bars
        ScaleIn,             // Enter in portions at multiple levels
        BreakoutEntry        // Enter on break of level with volume confirmation
    }

    /// <summary>
    /// Exit management strategies
    /// </summary>
    public enum ExitStrategyType
    {
        None,
        FixedTarget,     // Exit at predetermined target
        TrailingStop,    // ATR-based trailing stop
        StructureBreak,  // Exit on break of swing structure
        TimeBasedExit,   // Exit after N bars if no movement
        ScaleOut         // Exit in portions at R-multiples
    }

    /// <summary>
    /// Trade direction for setup results
    /// </summary>
    public enum TradeDirection
    {
        Long,
        Short,
        None
    }
}