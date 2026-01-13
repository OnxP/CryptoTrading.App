namespace CryptoTrading.App.Core
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