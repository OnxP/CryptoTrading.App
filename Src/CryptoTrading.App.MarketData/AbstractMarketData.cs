using Binance;
using Binance.Client;
using CryptoTrading.App.Core.TradeRequest;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Options;
using MailKit.Net.Smtp;
using MimeKit;
using MimeKit.Text;

namespace CryptoTrading.App.MarketData
{
    public abstract class AbstractMarketData : IMarketDataEvents
    {
        protected static readonly object Sync = new object();
        public IConfig Config { get; set; }
        protected void HandleError(Exception e)
        {
            lock (Sync)
            {
                Console.WriteLine(e.Message);

                Emailer.SendError(e.Message, Config);

                Task.Delay(60 * 1000);
                //wait some time and try and reconnect...wait 1 min.
                Configure(Config);
                //also email!
            }
        }
        protected IDictionary<(string symbol, CandlestickInterval interval), IList<Action<IEnumerable<Candlestick>>>> historicDataSubscribers = 
            new Dictionary<(string symbol, CandlestickInterval interval), IList<Action<IEnumerable<Candlestick>>>>();

        protected IDictionary<(string symbol, CandlestickInterval interval), IList<Action<CandlestickEventArgs>>> subscribers = 
            new Dictionary<(string symbol, CandlestickInterval interval), IList<Action<CandlestickEventArgs>>>();
        //public events 

        public void InitialDataLoadSubscribe(string symbol, CandlestickInterval interval, Action<IEnumerable<Candlestick>> callback)
        {
            if (!historicDataSubscribers.ContainsKey((symbol, interval)))
            {
                historicDataSubscribers.Add((symbol, interval), new List<Action<IEnumerable<Candlestick>>>());
            }
            historicDataSubscribers[(symbol, interval)].Add(callback);
        }
        public void InitialDataLoadUnSubscribe(string symbol, CandlestickInterval interval)
        {
            historicDataSubscribers.Remove((symbol, interval));
        }

        public void InitialDataStreamSubscribe(string symbol, CandlestickInterval interval, Action<CandlestickEventArgs> callback)
        {
            if (!subscribers.ContainsKey((symbol, interval)))
            {
                subscribers.Add((symbol, interval), new List<Action<CandlestickEventArgs>>());
            }
            subscribers[(symbol, interval)].Add(callback);
        }

        public void InitialDataStreamUnSubscribe(string symbol, CandlestickInterval interval)
        {
            subscribers.Remove((symbol, interval));
        }

        public abstract void Configure(IConfig request);
    }

    public class Emailer
    {
        public static void SendError(string eMessage, IConfig config)
        {
            using var smtpClient = new SmtpClient();
            smtpClient.Connect(config.EmailServer, config.EmailPort, true);
            smtpClient.Authenticate(config.EmailFrom, config.EmailPassword);
            var mail = CreateEmail(eMessage, config);
            smtpClient.Send(mail);
            smtpClient.Disconnect(true);
        }

        private static MimeMessage CreateEmail(string message, IConfig config)
        {
            var mail = new MimeMessage();
            mail.From.Add(new MailboxAddress("Ankur", config.EmailFrom));
            mail.To.Add(new MailboxAddress("Ankur", config.EmailTo));
            mail.Subject = $"Crypto Trading API Error";
            mail.Body = new TextPart(TextFormat.Plain)
            {
                Text = message
            };
            return mail;
        }
    }
}
