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
            switch (Config.RunType)
            {
                case RunTypeEnum.BackTesting:
                    historicTrades.Add(new BackTestingCompletedTrades(trade));
                    break;
                case RunTypeEnum.LiveTesting:
                    historicTrades.Add(new LiveTestingCompletedTrades(trade));
                    break;
                case RunTypeEnum.Live:
                    historicTrades.Add(new CompletedTrades(trade));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
