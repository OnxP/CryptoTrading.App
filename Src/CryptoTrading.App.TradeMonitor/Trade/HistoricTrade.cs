using CryptoTrading.App.Monitor.Position;
using System;

namespace CryptoTrading.App.Monitor.Trade
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
            SoldPrice = Convert.ToDouble(trade.ClosePrice);
            BoughtPrice = Convert.ToDouble(trade.OpenPrice);
            Symbol = trade.Pair;
            Quantity = Convert.ToDouble(trade.TotalOpenBaseQuantity);
            Profit = Convert.ToDouble(trade.ProfitPct);
            BtcProfit = Convert.ToDouble(trade.Profit);
            FeeBnb = Convert.ToDouble(trade.FeeBnb);
            StartDate = trade.StartDate;
            CloseDate = trade.CloseDate;
            Comment = trade.Comment;
        }

        public HistoricTrades(HistoricTradeRecord record)
        {
            SoldPrice = Convert.ToDouble(record.ExitPrice);
            BoughtPrice = Convert.ToDouble(record.EntryPrice);
            Symbol = record.Symbol;
            Quantity = Convert.ToDouble(record.Quantity);
            Profit = Convert.ToDouble(record.ProfitPct);
            BtcProfit = Convert.ToDouble(record.Profit);
            StartDate = record.EntryTime;
            CloseDate = record.ExitTime;
        }
    }

    public class BackTestingCompletedTrades : HistoricTrades
    {
        public BackTestingCompletedTrades(ITrade trade) : base(trade) { }
        public BackTestingCompletedTrades(HistoricTradeRecord record) : base(record) { }
    }

    public class LiveTestingCompletedTrades : HistoricTrades
    {
        public LiveTestingCompletedTrades(ITrade trade) : base(trade) { }
        public LiveTestingCompletedTrades(HistoricTradeRecord record) : base(record) { }
    }

    public class CompletedTrades : HistoricTrades
    {
        public CompletedTrades(ITrade trade) : base(trade) { }
        public CompletedTrades(HistoricTradeRecord record) : base(record) { }
    }
}
