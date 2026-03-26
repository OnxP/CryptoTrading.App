namespace CryptoTrading.App.Core.Strategy
{
    public class StrategyStatus
    {
        public StrategyAction StrategyAction { get; set; }
        public StrategyState StrategyState { get; set; }

        /// <summary>
        /// Cached exit decision from ProcessStrategy to avoid calling GetNextExit twice per candle.
        /// </summary>
        public TradeDetails ExitDetails { get; set; }
    }
}