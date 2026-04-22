using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
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

        // PR 5c: dictionaries now key on the neutral CandleInterval and store
        // callbacks in terms of ExchangeCandlestick / ExchangeCandlestickEvent.
        // Concrete implementations (Live / Historical / Db) emit these types
        // directly; no boundary translation happens at the subscribe seam.
        protected IDictionary<(string symbol, CandleInterval interval), IList<Action<IEnumerable<ExchangeCandlestick>>>> historicDataSubscribers =
            new Dictionary<(string symbol, CandleInterval interval), IList<Action<IEnumerable<ExchangeCandlestick>>>>();

        protected IDictionary<(string symbol, CandleInterval interval), IList<Action<ExchangeCandlestickEvent>>> subscribers =
            new Dictionary<(string symbol, CandleInterval interval), IList<Action<ExchangeCandlestickEvent>>>();

        public void InitialDataLoadSubscribe(string symbol, CandleInterval interval, Action<IEnumerable<ExchangeCandlestick>> callback)
        {
            if (!historicDataSubscribers.ContainsKey((symbol, interval)))
            {
                historicDataSubscribers.Add((symbol, interval), new List<Action<IEnumerable<ExchangeCandlestick>>>());
            }
            historicDataSubscribers[(symbol, interval)].Add(callback);
        }
        public void InitialDataLoadUnSubscribe(string symbol, CandleInterval interval)
        {
            historicDataSubscribers.Remove((symbol, interval));
        }

        public void InitialDataStreamSubscribe(string symbol, CandleInterval interval, Action<ExchangeCandlestickEvent> callback)
        {
            if (!subscribers.ContainsKey((symbol, interval)))
            {
                subscribers.Add((symbol, interval), new List<Action<ExchangeCandlestickEvent>>());
            }
            subscribers[(symbol, interval)].Add(callback);
        }

        public void InitialDataStreamUnSubscribe(string symbol, CandleInterval interval)
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
