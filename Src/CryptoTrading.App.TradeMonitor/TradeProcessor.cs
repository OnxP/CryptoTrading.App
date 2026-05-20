using CryptoTrading.App.Broker.Position;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Monitor.Position;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.Monitor
{
    public class TradeProcessor : ITradeProcessor
    {
        public IPositions Positions { get; set; }
        private IMarketMonitorFactory TradeFactory { get; }
        public List<ITradeMonitor> OrderMonitors { get; set; }
        public string KeyValue { get; set; }
        public IEnumerable<ITradeMonitor> CurrentMonitors => OrderMonitors.Where(x => x.Live).ToList();

        private ILogger<TradeProcessor> Logger { get; set; }

        public TradeProcessor(ILogger<TradeProcessor> logger, IMarketMonitorFactory factory)
        {
            TradeFactory = factory;
            OrderMonitors = new List<ITradeMonitor>();
            KeyValue = string.IsNullOrEmpty(KeyValue) ? "1" : KeyValue;
            Logger = logger;
            ConfigureMessageBroker();
        }

        public TradeProcessor(ILogger<TradeProcessor> logger, IPositions positions, IMarketMonitorFactory factory)
            : this(logger, factory)
        {
            Positions = positions;
        }

        public TradeProcessor(IPositions positions, ILogger<TradeProcessor> logger, IMarketMonitorFactory factory, IKey key)
            : this(logger, positions, factory)
        {
            KeyValue = key.KeyValue;
        }

        private void ConfigureMessageBroker()
        {
            IMessageBroker messageBroker = MessageBroker.Instance;

            Func<MessagePayload<ITradeSignal>, Task> signalHandler = ProcessSignal;
            messageBroker.Subscribe(KeyValue, signalHandler);
        }

        private async Task ProcessSignal(MessagePayload<ITradeSignal> payload)
        {
            var signal = payload.What;
            var symbol = signal.Symbol;
            if (string.IsNullOrEmpty(symbol))
                symbol = signal.BaseSymbol + signal.QuoteSymbol;

            var existingMonitor = OrderMonitors.LastOrDefault(x => x.Symbol == symbol);
            if (existingMonitor != null)
            {
                await existingMonitor.SetNewSignal(signal);
            }
            else if (CurrentMonitors.Count() <= Config.NoOfTrades)
            {
                var tradeMonitor = await TradeFactory.CreateMonitor(signal);
                tradeMonitor.KeyValue = KeyValue;
                OrderMonitors.Add(tradeMonitor);
            }
        }

        public void CompleteAllTransactions()
        {
            OrderMonitors.ToList().ForEach(x => x.CompleteTrade());
        }

        public void ClearInactiveTrades()
        {
            OrderMonitors.RemoveAll(x => !x.Live);
        }

        public List<HistoricTradeRecord> GetCompletedTrades()
        {
            return OrderMonitors.SelectMany(x => x.CompletedTrades).ToList();
        }

        public IConfig Config { get; set; }
        public void Configure(IConfig config)
        {
            Config = config;
        }
    }
}
