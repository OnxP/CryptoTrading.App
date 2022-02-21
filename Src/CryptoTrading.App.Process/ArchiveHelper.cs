using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database.StoreTrades;
using CryptoTrading.App.Core.Trade;
using MailKit.Net.Smtp;
using MimeKit;

namespace CryptoTrading.App.Process
{
    internal class ArchiveHelper
    {
        public static void StoreTradesToDb(List<HistoricTrades> completedTrades, IConfig config)
        {
            using var context = new HistoricTradeContext(config.StoreTradesConnectionString);
            completedTrades.ForEach(x => context.Trades.AddRange(completedTrades));
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
            new TextPart("html")
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
            sb.Append(String.Join(',', "Bought Price", "Sold Price", "Quantity", "Start Date", "Close Date", "Profit"));
            sb.Append(Environment.NewLine);
            trades.ForEach(x=>sb.Append(String.Join(',', x.BoughtPrice, x.SoldPrice,x.Quantity, x.StartDate, x.CloseDate, x.Profit,Environment.NewLine)));
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
            sb.Append($"Total Profit: [{completedTrades.Sum(x => x.Profit)}]%");
            sb.Append(Environment.NewLine);
            return sb.ToString();
        }
    }
}
