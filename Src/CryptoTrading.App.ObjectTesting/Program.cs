// See https://aka.ms/new-console-template for more information

using Binance;
using CryptoTrading.App.Core;
using MimeKit;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

var dic = new CandleStickDictionary(5);

var openDateTime = new DateTime(2021, 09, 10, 10, 00, 00);
dic.Add(
    new Candlestick("TEST", CandlestickInterval.Minutes_15, openDateTime, 0.1m, 0.2m, 0.1m,
        0.15m, 10, new DateTime(2021, 09, 10, 10, 15, 00), 0m, 10, 10, 10));

openDateTime = new DateTime(2021, 09, 10, 10, 15, 00);
dic.Add(
    new Candlestick("TEST", CandlestickInterval.Minutes_15, openDateTime, 0.1m, 0.2m, 0.1m,
        0.15m, 10, new DateTime(2021, 09, 10, 10, 30, 00), 0m, 10, 10, 10));

openDateTime = new DateTime(2021, 09, 10, 10, 30, 00);
dic.Add(
    new Candlestick("TEST", CandlestickInterval.Minutes_15, openDateTime, 0.1m, 0.2m, 0.1m,
        0.15m, 10, new DateTime(2021, 09, 10, 10, 45, 00), 0m, 10, 10, 10));

openDateTime = new DateTime(2021, 09, 10, 10, 45, 00);
dic.Add(
    new Candlestick("TEST", CandlestickInterval.Minutes_15, openDateTime, 0.1m, 0.2m, 0.1m,
        0.15m, 10, new DateTime(2021, 09, 10, 11, 00, 00), 0m, 10, 10, 10));


foreach (var candle in dic.GroupCandleSticks(2))
{
    Console.WriteLine(candle.OpenTime.ToString("s"));
}

EmailTrades();


void EmailTrades()
{
    using var smtpClient = new SmtpClient();
    smtpClient.Connect("smtp.gmail.com", 465,true);
    smtpClient.Authenticate("onx.patel@gmail.com", "nowgxuyliqbhmvwe");
    smtpClient.Send(CreateEmail());
    smtpClient.Disconnect(true);
}

static MimeMessage CreateEmail()
{
    var mail = new MimeMessage();
    mail.From.Add(new MailboxAddress("Ankur", "onx.patel@gmail.com"));
    mail.To.Add(new MailboxAddress("Ankur", "ankurpatel0000@hotmail.com"));
    mail.Subject = $"Test";
    mail.Body = new TextPart("html")
    {
        Text = "Plain"
    };
    return mail;
}
