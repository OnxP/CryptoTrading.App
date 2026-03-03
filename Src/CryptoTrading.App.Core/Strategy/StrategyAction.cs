namespace CryptoTrading.App.Core.Strategy
{
    public enum StrategyAction
    {
        OpenTrade,
        CloseTrade,
        NoAction,
    }
    public enum StrategyState
    {
        WaitingForEntry,
        EntrySubmitted,
        EntryPartiallyFilled,
        EntryFilled,
        WaitingForExit,
        ExitSubmitted, ExitPartiallyFilled, ExitFilled
    }
}