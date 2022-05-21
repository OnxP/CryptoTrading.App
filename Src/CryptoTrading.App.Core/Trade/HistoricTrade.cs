using System;

namespace CryptoTrading.App.Core.Trade
{
    public class HistoricTrades
    {
        public int Id { get; set; }
        public double SoldPrice { get; set; }
        public string Symbol { get; set; }
        public double Quantity { get; set; }
        public double Profit { get; set; }
        public double BtcProfit { get; set; }
        public double BoughtPrice { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime CloseDate { get; set; }
        public string Comment { get; set; }

        public double FeeBnb { get; set; }

        public HistoricTrades(ITrade trade)
        {
            SoldPrice = Convert.ToDouble(trade.Price);
            Symbol = trade.Pair;
            Quantity = Convert.ToDouble(trade.Quantity);
            Profit = Convert.ToDouble(trade.Profit);
            BtcProfit = Convert.ToDouble(trade.BtcProfit);
            BoughtPrice = Convert.ToDouble(trade.StartPrice);
            StartDate = trade.StartDate;
            CloseDate = trade.CloseDate;
            Comment = trade.Comment;
        }
    }

    public class BackTestingCompletedTrades : HistoricTrades
    {
        public BackTestingCompletedTrades(ITrade trade): base(trade)
        {
        }
    }

    public class LiveTestingCompletedTrades : HistoricTrades
    {
        public LiveTestingCompletedTrades(ITrade trade):base(trade)
        {
        }
    }

    public class CompletedTrades : HistoricTrades
    {
        public CompletedTrades(ITrade trade):base(trade)
        {
        }
    }
}
