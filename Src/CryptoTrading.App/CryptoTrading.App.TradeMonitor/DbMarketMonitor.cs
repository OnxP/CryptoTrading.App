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
        public DbMarketMonitor(ICandleStickManagement management, IDbData data)
        {
            _mangement = management;
            _data = data;
            //_mangement.AddMonitor(this);
        }
        private System.Action<CandlestickEventArgs> action;

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
  WHERE OpenTime >= @p0 AND OpenTime <= @p1 AND Symbol=@p2 AND Interval=@p3
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
        IDbData _data;
        public void StartStream()
        {
            Started = true;
            //DbData _data = DbData.GetInstance;
            _data.LoadData(SQL_STREAM_QUERY, _mangement.CurrentTick, _mangement.FinalTick, Symbol, 0);
            _mangement.AddStopLimitStream(InvokeCandleStick);
            DbCandleStickManagement.PauseFlow = false;
        }

        public void InvokeCandleStick()
        {

            var candleSticks = _data.GetData(Symbol,_mangement.CurrentTick);
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
