using System;

namespace CryptoTrading.App.Core.Trade
{
    public class HistoricTrades
    {
        public int Id { get; set; }
        public decimal SoldPrice { get; set; }
        public string Symbol { get; set; }
        public decimal Quantity { get; set; }
        public decimal Profit { get; set; }
        public decimal BoughtPrice { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime CloseDate { get; set; }
        public HistoricTrades(ITrade trade)
        {
            SoldPrice = trade.CurrentPrice;
            Symbol = trade.Symbol;
            Quantity = trade.Quantity;
            Profit = trade.Profit;
            BoughtPrice = trade.Price;
            StartDate = trade.StartDate;
            CloseDate = trade.CloseDate;
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
