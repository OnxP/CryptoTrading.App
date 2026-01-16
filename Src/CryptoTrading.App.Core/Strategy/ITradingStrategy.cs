namespace CryptoTrading.App.Core.Strategy
{
    public interface ITradingStrategy
    {
        //public Dictionary<string, (Indicator indicator, double[] options)> Indicators { get; }
        int OutputLength { get; }

        double Calculate(CandleStickDictionary candleSticks, IStopLimitTracker stopLimitTrackers);
        void Log(string v);
    }
}
