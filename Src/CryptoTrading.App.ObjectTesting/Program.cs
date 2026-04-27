// See https://aka.ms/new-console-template for more information

using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using MimeKit;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

// PR 5h: directly construct neutral ExchangeCandlestick — the bridge is gone.
static ExchangeCandlestick NewCandle(DateTime openTime, DateTime closeTime) => new ExchangeCandlestick
{
    Symbol = "TEST",
    Interval = CandleInterval.Minute_15,
    OpenTime = openTime,
    CloseTime = closeTime,
    Open = 0.1m,
    High = 0.2m,
    Low = 0.1m,
    Close = 0.15m,
    Volume = 10m,
    QuoteVolume = 0m,
    NumberOfTrades = 10,
    IsClosed = true,
};

var dic = new CandleStickDictionary(5);

dic.Add(NewCandle(new DateTime(2021, 09, 10, 10, 00, 00), new DateTime(2021, 09, 10, 10, 15, 00)));
dic.Add(NewCandle(new DateTime(2021, 09, 10, 10, 15, 00), new DateTime(2021, 09, 10, 10, 30, 00)));
dic.Add(NewCandle(new DateTime(2021, 09, 10, 10, 30, 00), new DateTime(2021, 09, 10, 10, 45, 00)));
dic.Add(NewCandle(new DateTime(2021, 09, 10, 10, 45, 00), new DateTime(2021, 09, 10, 11, 00, 00)));


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
