using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.TradeRequest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.MarketData
{
    public class DbMarketData : AbstractMarketData, IMarketData
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        CryptoDBContext context;

        private string SQL_HISTORIC_QUERY = @"SELECT Top 100 [ID]
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
  FROM [dbo].[MyCandleSticks]
  WHERE OpenTime >= @p0 AND Symbol=@p1 AND Interval=@p2
  ORDER BY OpenTime";
        private string SQL_STREAM_QUERY = @"SELECT [ID]
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
  FROM [dbo].[MyCandleSticks]
  WHERE OpenTime >= @p0 AND Symbol=@p1 AND Interval=@p2
  ORDER BY OpenTime
OFFSET 100 ROWS";

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
                Console.WriteLine("  ...press any key to close window.");
                Console.ReadKey(true);
            }
        }

        private void StreamData()
        {
            
            foreach (var item in subscribers)
            {
                var candleSticks = context.CandleSticks.SqlQuery(SQL_STREAM_QUERY, From, item.Key.symbol, item.Key.interval).ToListAsync().Result;
                candleSticks.ForEach(x => candleSticksToStream.Add((CandleStickDb.ConvertObject(x),item.Key.interval)));
            }

            var orderedList = candleSticksToStream.OrderBy(x => x.candlestick.CloseTime);

            foreach (var item in orderedList.GroupBy(x => x.candlestick.CloseTime))
            {
                foreach (var candleStick in item)
                {
                    foreach (var action in subscribers[(candleStick.candlestick.Symbol, candleStick.interval)])
                    {
                        action.Invoke(new CandlestickEventArgs(item.Key, candleStick.candlestick, 0, 0, true));
                    }
                }
            }
        }
        List<(Candlestick candlestick, CandlestickInterval interval)> candleSticksToStream = new List<(Candlestick, CandlestickInterval interval)>();
        private void LoadHistoricData()
        {
            foreach (var item in historicDataSubscribers)
            {
                LoadHistoricData( item.Key, From, item.Value);
            }
        }
        private void LoadHistoricData((string symbol, CandlestickInterval interval) symbol, DateTime from, Action<IEnumerable<Candlestick>> callback)
        {
            var candleSticks = context.CandleSticks.SqlQuery(SQL_HISTORIC_QUERY, from, symbol.symbol, symbol.interval).ToListAsync().Result;
            //need to drop first candle
            //candleSticks.Reverse();
            List<Candlestick> sticks = new List<Candlestick>();
            candleSticks.ForEach(x => sticks.Add(CandleStickDb.ConvertObject(x)));
            callback.Invoke(sticks);
        }
    }
}
