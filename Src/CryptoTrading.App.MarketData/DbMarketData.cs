using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.TradeRequest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoTrading.App.MarketData
{
    public class DbMarketData : AbstractMarketData, IMarketData
    {
        ICandleStickManagement _mangement;
        public DbMarketData(ICandleStickManagement management)
        {
            _mangement = management;
            //_mangement.AddMarketData(this);
        }

        public DateTime From { get; set; }
        public DateTime To { get; set; }

        CryptoDBContext context;

        private string SQL_HISTORIC_QUERY = @"SELECT DISTINCT Top 300 [ID]
      ,[Symbol]
      ,[Interval]
      ,[OpenTime]
      ,[Open]
      ,[High]
      ,[Low]
      ,[Close]
      ,[Volume]
      ,[CloseTime]
      ,[QuoteAssetVolume]
      ,[NumberOfTrades]
      ,[TakerBuyBaseAssetVolume]
      ,[TakerBuyQuoteAssetVolume]
  FROM [dbo].[CandleStickDbs]
  WHERE OpenTime < @p0 AND Symbol=@p1 AND Interval=@p2
  ORDER BY OpenTime desc";
        private string SQL_STREAM_QUERY = @"SELECT DISTINCT [ID]
      ,[Symbol]
      ,[Interval]
      ,[OpenTime]
      ,[Open]
      ,[High]
      ,[Low]
      ,[Close]
      ,[Volume]
      ,[CloseTime]
      ,[QuoteAssetVolume]
      ,[NumberOfTrades]
      ,[TakerBuyBaseAssetVolume]
      ,[TakerBuyQuoteAssetVolume]
  FROM [dbo].[CandleStickDbs]
  WHERE OpenTime >= @p0 AND openTime <= @p1 AND Symbol=@p2 AND Interval=@p3
  ORDER BY OpenTime";

        public override void Configure(IRequest request)
        {
            context = new CryptoDBContext();
        }

        public void StartStream()
        {
            try
            {
                LoadHistoricData();
                StreamData();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine();
            }
        }

        private void StreamData()
        {
            
            foreach (var item in subscribers)
            {
                int interval = (int)item.Key.interval;
                var candleSticks = context.CandleSticks.SqlQuery(SQL_STREAM_QUERY, From, To, item.Key.symbol, interval).ToListAsync().Result;
                candleSticks.ForEach(x => candleSticksToStream.Add((CandleStickDb.ConvertObject(x), item.Key.interval)));
            }

            orderedList = candleSticksToStream.OrderBy(x => x.candlestick.CloseTime).GroupBy(x => x.candlestick.CloseTime);

            _mangement.BuildTimeKeeper(orderedList.Min(x=>x.Key), orderedList.Max(x => x.Key));

            _mangement.AddMarketStream(InvokeCandleStick);

            _mangement.StartTimeKeeper();
        }
        IEnumerable<IGrouping<DateTime, (Candlestick candlestick, CandlestickInterval interval)>> orderedList;
        public void InvokeCandleStick()
        {
            var tasks = new List<Task>();
            
            var candleSticks = orderedList.FirstOrDefault(x => x.Key == _mangement.CurrentTick);
            if (candleSticks == null) return;
            foreach (var candleStick in candleSticks)
            {
                foreach (var action in subscribers[(candleStick.candlestick.Symbol, candleStick.interval)])
                {
                    tasks.Add(new Task(()=>action.Invoke(new CandlestickEventArgs(candleSticks.Key, candleStick.candlestick, 0, 0, true))));
                }
            }
            tasks.AsParallel().ForAll(x => x.Start());
            Task.WaitAll(tasks.ToArray());
            //runs the algo but the request to the process monitor takes some time to execute due to the checks that it does.
            Thread.Sleep(100);
            while (DbCandleStickManagement.PauseFlow)
            {
                //pause the flow for execution, specific to db use only.
            }
        }

        public List<(Candlestick candlestick, CandlestickInterval interval)> candleSticksToStream = new List<(Candlestick, CandlestickInterval interval)>();
        private void LoadHistoricData()
        {
            foreach (var item in historicDataSubscribers)
            {
                LoadHistoricData( item.Key, From, item.Value);
            }
        }
        private void LoadHistoricData((string symbol, CandlestickInterval interval) symbol, DateTime from, IList<Action<IEnumerable<Candlestick>>> callback)
        {
            int interval = (int)symbol.interval;
            var candleSticks = context.CandleSticks.SqlQuery(SQL_HISTORIC_QUERY, from, symbol.symbol, interval).ToListAsync().Result;
            var cs = candleSticks.OrderBy(x => x.OpenTime).ToList();
            //need to drop first candle
            //candleSticks.Reverse();
            List<Candlestick> sticks = new List<Candlestick>();
            cs.ForEach(x => sticks.Add(CandleStickDb.ConvertObject(x)));
            foreach (var action in callback)
            {
                action.Invoke(sticks);
            }
        }
    }
}
