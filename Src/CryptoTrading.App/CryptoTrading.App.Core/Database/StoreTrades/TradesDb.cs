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
        public decimal Risk { get; set; }
        public decimal Increment { get; set; }

        public TradesDb(ITrade trade, Indicator strat, double trades, decimal risk, decimal increment)
        {
            Strategy = strat.FullName;
            NoOfTrades = trades;
            Risk = risk;
            Increment = increment;

            Price = trade.Price;
            Symbol = trade.Symbol;
            Quantity = trade.Quantity;
            Profit = trade.Profit;
            StartPrice = trade.StartPrice;
            StartDate = trade.StartDate;
            CloseDate = trade.CloseDate;
        }
        public decimal Price { get; set; }
        public string Symbol { get; set; }
        public decimal Quantity { get; set; }
        public decimal Profit { get; set; }
        public decimal StartPrice { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime CloseDate { get; set; }
    }
}