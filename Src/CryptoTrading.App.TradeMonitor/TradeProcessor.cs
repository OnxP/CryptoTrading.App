using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
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
        private IMarketMonitorFactory TradeFactory {get;}
        public List<ITradeMonitor> OrderMonitors { get; set; }
        public IEnumerable<ITrade> LiveTrades => OrderMonitors.Where(x => x.Live).Select(x=>x.HistoricTrades.Last());
        public string KeyValue { get; set; }
        public IEnumerable<ITradeMonitor> CurrentMonitors
        {
            get
            {
                return OrderMonitors.Where(x => x.Live).ToList();
            }
        }

        private ILogger<TradeProcessor> Logger { get; set; }
        public TradeProcessor(ILogger<TradeProcessor> logger ,IMarketMonitorFactory factory)
        {
            TradeFactory = factory;
            OrderMonitors = new List<ITradeMonitor>();
            KeyValue = string.IsNullOrEmpty(KeyValue) ? "1" : KeyValue;
            Logger = logger;
            ConfigureMessageBroker();
        }
        public TradeProcessor(ILogger<TradeProcessor> logger,IPositions positions, IMarketMonitorFactory factory):this(logger,factory)
        {
            Positions = positions;
        }

        public TradeProcessor(IPositions positions, ILogger<TradeProcessor> logger,IMarketMonitorFactory factory, IKey key): this(logger,positions, factory)
        {
            KeyValue = key.KeyValue;
        }
        private void ConfigureMessageBroker()
        {
            IMessageBroker messageBroker = MessageBroker.Instance;

            Func<MessagePayload<ITradeRequest>, Task> TradeRequestMessage = ProcessMessageAction;
            messageBroker.Subscribe(KeyValue, TradeRequestMessage);
        }

        private async Task ProcessMessageAction(MessagePayload<ITradeRequest> obj)
        {
            var symbol = obj.What.BaseSymbol + obj.What.QuoteSymbol;

            // First check: is there an existing monitor for this symbol (Live or not)?
            // A non-Live monitor still has its 1M subscription active and should receive
            // fresh setups via SetNewRequest so it can trade again with updated SL/TP.
            var existingMonitor = OrderMonitors.LastOrDefault(x => x.Symbol == symbol);
            if (existingMonitor != null)
            {
                await existingMonitor.SetNewRequest(obj.What);
            }
            else if (Positions.CheckRequest(obj.What) && LiveTrades.Count()<=Config.NoOfTrades)
            {
                var tradeMonitor = await TradeFactory.CreateMonitor(obj.What, Positions);
                tradeMonitor.KeyValue = KeyValue;
                OrderMonitors.Add(tradeMonitor);
            }
        }

        private bool CheckCurrentOrderMonitors(string symbol)
        {
            if (CurrentMonitors.Count() == 0) return false;
            return CurrentMonitors.LastOrDefault(x => x.Symbol == symbol) != null && CurrentMonitors.LastOrDefault(x => x.Symbol == symbol)!.Live;
        }

        public void CompleteAllTransactions()
        {
            OrderMonitors.ToList().ForEach(x=>x.CompleteTrade());
        }

        public void ClearInactiveTrades()
        {
            OrderMonitors.RemoveAll(x => !x.Live);
        }

        public List<ITrade> GetCompletedTrades()
        {
            return OrderMonitors.SelectMany(x=>x.HistoricTrades).ToList();
        }
        public IConfig Config { get; set; }
        public void Configure(IConfig config)
        {
            Config = config;
        }
    }
}

