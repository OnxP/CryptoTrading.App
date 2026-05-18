using CryptoTrading.App.Monitor.Position;
using System;
using Tulip;

namespace CryptoTrading.App.Monitor.Trade
{
    public class TradesDb
    {
        public int ID { get; set; }
        public string Strategy { get; set; }
        public double NoOfTrades { get; set; }
        public double Risk { get; set; }
        public double Increment { get; set; }

        public TradesDb(ITrade trade, Indicator strat, double trades, decimal risk, decimal increment)
        {
            Strategy = strat.FullName;
            NoOfTrades = trades;
            Risk = Convert.ToDouble(risk);
            Increment = Convert.ToDouble(increment);

            Price = Convert.ToDouble(trade.ClosePrice);
            Symbol = trade.Pair;
            Quantity = Convert.ToDouble(trade.TotalOpenBaseQuantity);
            Profit = Convert.ToDouble(trade.ProfitPct);
            StartPrice = Convert.ToDouble(trade.OpenPrice);
            StartDate = trade.StartDate;
            CloseDate = trade.CloseDate;
        }

        public TradesDb(HistoricTradeRecord record, Indicator strat, double trades, decimal risk, decimal increment)
        {
            Strategy = strat.FullName;
            NoOfTrades = trades;
            Risk = Convert.ToDouble(risk);
            Increment = Convert.ToDouble(increment);

            Price = Convert.ToDouble(record.ExitPrice);
            Symbol = record.Symbol;
            Quantity = Convert.ToDouble(record.Quantity);
            Profit = Convert.ToDouble(record.ProfitPct);
            StartPrice = Convert.ToDouble(record.EntryPrice);
            StartDate = record.EntryTime;
            CloseDate = record.ExitTime;
        }

        public double Price { get; set; }
        public string Symbol { get; set; }
        public double Quantity { get; set; }
        public double Profit { get; set; }
        public double StartPrice { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime CloseDate { get; set; }
    }
}
