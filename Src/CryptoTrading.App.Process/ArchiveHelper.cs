using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database.StoreTrades;
using CryptoTrading.App.Core.Trade;
using CoreBacktestMetrics = CryptoTrading.App.Core.Trade.BacktestMetrics;
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

            var bodyText = GenerateEmailBody(completedTrades);
            var textPart = new TextPart(TextFormat.Plain) { Text = bodyText };
            var multipart = new Multipart("mixed");
            multipart.Add(textPart);
            // Attach trade list as CSV
            if (completedTrades.Any())


            {
                var csvContent = GenerateCsv(completedTrades);
                var csvBytes = Encoding.UTF8.GetBytes(csvContent);
                var attachment = new MimePart("text", "csv")
                {
                    Content = new MimeContent(new MemoryStream(csvBytes)),
                    ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                    ContentTransferEncoding = ContentEncoding.Base64,
                    FileName = $"TradeList_{completedTrades.Min(x => x.StartDate):yyyyMMdd}_{completedTrades.Max(x => x.CloseDate):yyyyMMdd}.csv"
                };
                multipart.Add(attachment);

            }
            mail.Body = multipart;

            return mail;
        }

        private static string GenerateEmailBody(List<HistoricTrades> completedTrades)
        {
            var sb = new StringBuilder();

            sb.AppendLine(BuildSummary(completedTrades));
            sb.AppendLine(BuildRiskMetrics(completedTrades));
            sb.AppendLine(BuildMonthlySummary(completedTrades));

            return sb.ToString();
        }

        private static string BuildSummary(List<HistoricTrades> trades)
        {
            var count = trades.Count;
            if (count == 0) return "No trades.";

            var wins = trades.Count(x => x.Profit > 0);
            var losses = trades.Count(x => x.Profit < 0);
            var winPcts = trades.Where(x => x.Profit > 0).Select(x => x.Profit).ToList();
            var lossPcts = trades.Where(x => x.Profit < 0).Select(x => x.Profit).ToList();

            // Compute USDT equity: assume each trade's P&L % is applied to the running balance
            // Starting with the first trade's bought price * quantity as the position size
            var orderedTrades = trades.OrderBy(x => x.StartDate).ToList();
            double startUsdt = 0;
            double endUsdt = 0;
            if (orderedTrades.Count > 0)
            {
                // Estimate starting capital from the first trade's position value
                startUsdt = orderedTrades.First().BoughtPrice * orderedTrades.First().Quantity;
                // If that looks like a tiny BTC amount, use a default
                if (startUsdt < 1) startUsdt = 100000;

                double equity = startUsdt;
                foreach (var trade in orderedTrades)
                {
                    double positionValue = trade.BoughtPrice * trade.Quantity;
                    if (positionValue < 0.0001) positionValue = equity * 0.01; // fallback: 1% position
                    double tradePnl = positionValue * (trade.Profit / 100.0);
                    equity += tradePnl;
                }
                endUsdt = equity;
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== OVERALL SUMMARY ===");
            sb.AppendLine($"Start Amount:    {startUsdt:F2} USDT");
            sb.AppendLine($"Final Amount:    {endUsdt:F2} USDT");
            sb.AppendLine($"Net P&L:         {endUsdt - startUsdt:F2} USDT ({(startUsdt > 0 ? (endUsdt - startUsdt) / startUsdt * 100 : 0):F2}%)");
            sb.AppendLine();
            sb.AppendLine($"Total Trades:    {count}");
            sb.AppendLine($"Winning Trades:  {wins} ({(double)wins / count * 100:F1}%)");
            sb.AppendLine($"Losing Trades:   {losses} ({(double)losses / count * 100:F1}%)");
            sb.AppendLine($"Total Profit:    {trades.Sum(x => x.BtcProfit):F8} BTC");
            sb.AppendLine($"Total Fees:      {trades.Sum(x => x.FeeBnb):F4} BNB");
            sb.AppendLine($"Avg Win:         {(winPcts.Count > 0 ? winPcts.Average() : 0):F2}%");
            sb.AppendLine($"Avg Loss:        {(lossPcts.Count > 0 ? lossPcts.Average() : 0):F2}%");
            sb.AppendLine($"Best Trade:      {trades.Max(x => x.Profit):F2}%");
            sb.AppendLine($"Worst Trade:     {trades.Min(x => x.Profit):F2}%");
            return sb.ToString();
        }

        private static string BuildRiskMetrics(List<HistoricTrades> trades)
        {
            if (trades.Count == 0) return string.Empty;

            var pnls = trades.Select(x => (decimal)x.BtcProfit).ToList();
            var holdingHours = trades
                .Where(x => x.CloseDate > x.StartDate)
                .Select(x => (x.CloseDate - x.StartDate).TotalHours)
                .ToList();

            var metrics = CoreBacktestMetrics.Calculate(pnls, holdingHours);

            var sb = new StringBuilder();
            sb.AppendLine("=== RISK METRICS ===");
            sb.AppendLine($"Sharpe Ratio:         {metrics.SharpeRatio:F2}");
            sb.AppendLine($"Sortino Ratio:        {metrics.SortinoRatio:F2}");
            sb.AppendLine($"Calmar Ratio:         {metrics.CalmarRatio:F2}");
            sb.AppendLine($"Profit Factor:        {metrics.ProfitFactor:F2}");
            sb.AppendLine($"Expectancy/Trade:     {metrics.Expectancy:F8} BTC");
            sb.AppendLine($"Max Drawdown:         {metrics.MaxDrawdown:F8} BTC");
            sb.AppendLine($"Max Consec. Losses:   {metrics.MaxConsecutiveLosses}");
            sb.AppendLine($"Avg Holding Period:   {metrics.AverageHoldingPeriodHours:F1} hours");
            return sb.ToString();
        }

        private static string BuildMonthlySummary(List<HistoricTrades> trades)
        {
            if (trades.Count == 0) return string.Empty;

            var monthly = trades
                .Where(x => x.StartDate > DateTime.MinValue)
                .GroupBy(x => new { x.StartDate.Year, x.StartDate.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("=== MONTHLY BREAKDOWN ===");
            sb.AppendLine($"{"Month",-10} {"Trades",7} {"Wins",6} {"WinRate",8} {"Profit BTC",14} {"Avg P&L%",10} {"Best%",8} {"Worst%",8}");
            sb.AppendLine(new string('-', 75));

            foreach (var month in monthly)
            {
                var monthTrades = month.ToList();
                var wins = monthTrades.Count(x => x.Profit > 0);
                var label = $"{month.Key.Year}-{month.Key.Month:D2}";

                sb.AppendLine($"{label,-10} {monthTrades.Count,7} {wins,6} {(double)wins / monthTrades.Count * 100,7:F1}% {monthTrades.Sum(x => x.BtcProfit),14:F8} {monthTrades.Average(x => x.Profit),9:F2}% {monthTrades.Max(x => x.Profit),7:F2}% {monthTrades.Min(x => x.Profit),7:F2}%");
            }

            return sb.ToString();
        }

        private static string GenerateCsv(List<HistoricTrades> trades)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Symbol,Bought Price,Sold Price,Quantity,Start Date,Close Date,Profit,BtcProfit, BnbFee, Comment");
            foreach (var t in trades)
            {
                sb.AppendLine($"{t.Symbol},{t.BoughtPrice},{t.SoldPrice},{t.Quantity},{t.StartDate},{t.CloseDate},{t.Profit},{t.BtcProfit},{t.FeeBnb},{t.Comment}");
            }
            return sb.ToString();
        }
    }
}
