namespace CryptoTrading.App.Core.Strategy
{
    public enum VolatilityRegime
    {
        Low,
        Normal,
        High
    }

    public enum AllowedDirection
    {
        Long,
        Short,
        Both,
        None
    }

    public enum SetupType
    {
        None,
        TrendContinuationLong,
        TrendContinuationShort,
        MeanReversionLong,
        MeanReversionShort,
        BreakoutLong,
        BreakoutShort,
        FadeExtremeLong,
        FadeExtremeShort,
        MacdTrendLong,
        MacdTrendShort,
        BbMeanRevLong,
        BbMeanRevShort
    }

    public enum EntryStrategyType
    {
        None,
        LimitAtSupport,
        LimitAtResistance,
        MarketOnConfirmation,
        ScaleIn,
        BreakoutEntry,
        StochRsiEntry,
        LimitAtZoneEdge
    }

    public enum ExitStrategyType
    {
        None,
        FixedTarget,
        TrailingStop,
        StructureBreak,
        TimeBasedExit,
        ScaleOut
    }

    public enum ZoneType
    {
        Supply,
        Demand
    }
}
