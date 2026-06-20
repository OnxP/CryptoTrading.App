namespace CryptoTrading.App.Algorithm.DualRegime
{
    public sealed class DualRegimeTradingState
    {
        public decimal CurrentEquity { get; set; }
        public bool IsInPosition { get; set; }
        public int TotalTrades { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }

        public DualRegimeTradingState(decimal initialEquity)
        {
            CurrentEquity = initialEquity;
        }

        public string GetStatus() =>
            $"Equity:{CurrentEquity:F2} InPos:{IsInPosition} W:{Wins} L:{Losses} Total:{TotalTrades}";
    }
}
