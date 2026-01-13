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
        public IEnumerable<ITrade> LiveTrades => OrderMonitors.Where(x => x.Live).Select(x=>x.Trade);
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
            if(CheckCurrentOrderMonitors(obj.What.BaseSymbol + obj.What.QuoteSymbol))
            {
                //Need to update the execution strategy
                var monitor = CurrentMonitors.LastOrDefault(x => x.Symbol == obj.What.BaseSymbol + obj.What.QuoteSymbol);
                if (monitor != null)
                {
                    await monitor.SetNewRequest(obj.What);
                }
            }
            else if (Positions.CheckRequest(obj.What) && LiveTrades.Count()<=Config.NoOfTrades)
            {
                var tradeMonitor = await TradeFactory.CreateMonitor(obj.What, Positions);
                tradeMonitor.KeyValue = KeyValue;
                OrderMonitors.Add(tradeMonitor);
                //create Market Order
                //var marketOrder = new MarketRequest(trade.CurrentTransaction);
                //await MessageBroker.Instance.Publish<IMarketRequest>(KeyValue,trade.CurrentTransaction, marketOrder);
                //Logger.LogInformation($"Place Trade for {trade.Pair} Q: {trade.Quantity} BTC amt: {trade.CurrentTransaction.Quote.Quantity}");
            }
        }

        private bool CheckCurrentOrderMonitors(string symbol)
        {
            if (CurrentMonitors.Count() != 0) return false;
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
            return OrderMonitors.Where(x => !x.Live).Select(x=>x.Trade).ToList();
        }
        public IConfig Config { get; set; }
        public void Configure(IConfig config)
        {
            Config = config;
        }
    }
}

