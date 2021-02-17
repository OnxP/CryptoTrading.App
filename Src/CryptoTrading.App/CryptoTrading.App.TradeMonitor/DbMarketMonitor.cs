using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Trade;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CryptoTrading.App.Monitor
{
    //Class monitors the position in the open trade and adjusts the stop loss, this could work on live streaming data 
    //Input(Initial) - Trade details.
    //Input(continuous) - CandleStick Processing.
    //StaticInput - Stop loss type and limit.
    //Output - Change Stop Limit Order

    //Processing logic
    //Initial - Configure Stoploss Monitor from Open trade. and set a stop limit order.
    //Continuous - Monitor price and once it hits a threshold reset stoploss to limit order X% below threshold then adjust threshold

    public class DbMarketMonitor : IMarketMonitor
    {
        ICandleStickManagement _mangement;
        public DbMarketMonitor(ICandleStickManagement management)
        {
            _mangement = management;
            context = new CryptoDBContext();
            //_mangement.AddMonitor(this);
        }
        private System.Action<CandlestickEventArgs> action;

        CryptoDBContext context;
        public string Symbol { get; set; }
        public CandlestickInterval Interval { get; set; }

        public bool Started { get; private set; }

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
  FROM [dbo].[CandleStickDbs]
  WHERE OpenTime >= @p0 AND Symbol=@p1 AND Interval=@p2
  ORDER BY OpenTime";

        public bool CheckOrder(ITransaction transaction)
        {
            transaction.Complete();
            return true;
        }

        public void Dispose()
        {
            action = null;
            return;
        }

        public void StopStream()
        {
            _mangement.RemoveStopLimitStream(InvokeCandleStick);
        }
        public List<(Candlestick candlestick, CandlestickInterval interval)> candleSticksToStream = new List<(Candlestick, CandlestickInterval interval)>();

        public void StartStream()
        {
            Started = true;
            var candleSticks = context.CandleSticks.SqlQuery(SQL_STREAM_QUERY, _mangement.CurrentTick, Symbol, 0).ToListAsync().Result;
            if (candleSticks.Count == 0) throw new Exception("Bad Data.");
            candleSticks.ForEach(x => candleSticksToStream.Add((CandleStickDb.ConvertObject(x), Interval)));           

            orderedList = candleSticksToStream.OrderBy(x => x.candlestick.CloseTime).GroupBy(x => x.candlestick.CloseTime);

            _mangement.AddStopLimitStream(InvokeCandleStick);
        }

        IEnumerable<IGrouping<DateTime, (Candlestick candlestick, CandlestickInterval interval)>> orderedList;
        public void InvokeCandleStick()
        {
            var candleSticks = orderedList.FirstOrDefault(x => x.Key == _mangement.CurrentTick);
            if (candleSticks == null) return;
            foreach (var candleStick in candleSticks)
            {
                action.Invoke(new CandlestickEventArgs(candleSticks.Key, candleStick.candlestick, 0, 0, true));
            }
            
        }

        public void Subscribe(System.Action<CandlestickEventArgs> processCandleStick)
        {
            action = processCandleStick;
        }
    }


}
