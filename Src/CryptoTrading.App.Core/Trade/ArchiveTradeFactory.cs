using System;
using System.Collections.Generic;

namespace CryptoTrading.App.Core.Trade
{
    public class ArchiveTradeFactory
    {
        public ArchiveTradeFactory(IConfig config)
        {
            Config = config;
        }

        public IConfig Config { get; }

        public void CreateHistoricTrades(ITrade trade, List<HistoricTrades> historicTrades)
        {
            historicTrades.Add(CreateHistoricTrades(trade));
        }

        public HistoricTrades CreateHistoricTrades(ITrade trade)
        {
            switch (Config.RunType)
            {
                case RunTypeEnum.BackTesting:
                    return new BackTestingCompletedTrades(trade);
                case RunTypeEnum.LiveTesting:
                    return new LiveTestingCompletedTrades(trade);
                case RunTypeEnum.Live:
                    return new CompletedTrades(trade);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
