using Binance;
using CryptoTrading.App.Core.Trade;
using System;
using System.Collections.Generic;
using System.Text;
using Tulip;

namespace CryptoTrading.App.Core.Database.StoreTrades
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

            Price = Convert.ToDouble(trade.Price);
            Symbol = trade.Symbol;
            Quantity = Convert.ToDouble(trade.Quantity);
            Profit = Convert.ToDouble(trade.Profit);
            StartPrice = Convert.ToDouble(trade.StartPrice);
            StartDate = trade.StartDate;
            CloseDate = trade.CloseDate;
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