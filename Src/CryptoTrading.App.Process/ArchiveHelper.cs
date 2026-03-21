using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Database.StoreTrades;
using CryptoTrading.App.Core.Trade;
using MailKit.Net.Smtp;
using MimeKit;
using MimeKit.Text;

namespace CryptoTrading.App.Process
{
    internal class ArchiveHelper
    {
        public static void StoreTradesToDb(List<HistoricTrades> completedTrades, IConfig config)
        {
            using var context = new HistoricTradeContext(config.StoreTradesConnectionString);
            context.BulkInsert(completedTrades);
            context.SaveChanges();
        }

        public static void EmailTrades(List<HistoricTrades> completedTrades, IConfig config)
        {
            using var smtpClient = new SmtpClient();
            smtpClient.Connect(config.EmailServer, config.EmailPort, true);
            smtpClient.Authenticate(config.EmailFrom, config.EmailPassword);
            var mail = CreateEmail(completedTrades, config);
            smtpClient.Send(mail);
            smtpClient.Disconnect(true);
        }

        private static MimeMessage CreateEmail(List<HistoricTrades> completedTrades, IConfig config)
        {
            var mail = new MimeMessage();
            mail.From.Add(new MailboxAddress("Ankur", config.EmailFrom));
            mail.To.Add(new MailboxAddress("Ankur", config.EmailTo));
            mail.Subject = $"CryptoTrading Summary {completedTrades.Min(x => x.StartDate.ToShortDateString())} - {completedTrades.Max(x => x.CloseDate.ToShortDateString())}";
            mail.Body = new TextPart(TextFormat.Plain)
            {
                Text = GenerateHtmlBody(completedTrades, config)
            }; 
            return mail;
        }
        
        private static string GenerateHtmlBody(List<HistoricTrades> completedTrades, IConfig config)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(BuildSummary(completedTrades));

            builder.Append(PrintTrades(completedTrades));

            return builder.ToString();
        }

        private static string PrintTrades(List<HistoricTrades> trades)
        {
            var sb = new StringBuilder();
            sb.Append(Environment.NewLine);
            sb.Append(trades.ToStringTable(x => x.Symbol,
                x => x.BoughtPrice, //.FirstTransaction.Price.ToString("0.#########"), 
                x => x.SoldPrice,//.CurrentTransaction.Price.ToString("0.#########"), 
                x => x.Quantity, //CurrentTransaction.Base.Quantity.ToString("0.####"), 
                x => x.StartDate, //FirstTransaction.TransactionDate.ToString(), 
                x => x.CloseDate, //CurrentTransaction.TransactionDate.ToString(), 
                x => x.Profit,
                x=> x.BtcProfit,
                x=>x.FeeBnb,
                x=> x.Comment));
            sb.Append(Environment.NewLine);
            sb.Append(String.Join(',',"Symbol", "Bought Price", "Sold Price", "Quantity", "Start Date", "Close Date", "Profit","BtcProfit, BnbFee, Comment"));
            sb.Append(Environment.NewLine);
            trades.ForEach(x=>sb.Append(String.Join(',',x.Symbol, x.BoughtPrice, x.SoldPrice,x.Quantity, x.StartDate, x.CloseDate, x.Profit,x.BtcProfit, x.FeeBnb,x.Comment,Environment.NewLine)));
            return sb.ToString();
        }

        private static string BuildSummary(List<HistoricTrades> completedTrades)
        {
            var count = completedTrades.Count();
            var sb = new StringBuilder();
            sb.Append($"Total Number of Trades: [{count}]");
            sb.Append(Environment.NewLine);
            sb.Append($"Winning Trades: [{completedTrades.Count(x => x.Profit > 0)}] - {((double)completedTrades.Count(x => x.Profit > 0) / count) * 100}%");
            sb.Append(Environment.NewLine);
            sb.Append($"Losing Trades: [{completedTrades.Count(x => x.Profit < 0)}] - {((double)completedTrades.Count(x => x.Profit < 0) / count) * 100}%");
            sb.Append(Environment.NewLine);
            sb.Append($"Total Profit: [{completedTrades.Sum(x => x.BtcProfit)}] BTC");
            sb.Append(Environment.NewLine);
            sb.Append($"Total Fee BNB: [{completedTrades.Sum(x => x.FeeBnb)}] Bnb - [{completedTrades.Sum(x => x.FeeBnb)*0.010213d}] BTC");
            sb.Append(Environment.NewLine);
            return sb.ToString();
        }
    }
}
