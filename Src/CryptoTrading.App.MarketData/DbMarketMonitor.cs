using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Trade;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CryptoTrading.App.MarketData
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
            actions = new Dictionary<string, Dictionary<string,Action<CandlestickEventArgs>>>();
            //_mangement.AddMonitor(this);
        }

        public CandlestickInterval Interval { get; set; }

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
        private readonly object _lock = new object();
        public bool CheckOrder(ITransaction transaction)
        {
            transaction.Complete();
            return true;
        }

        public void Dispose()
        {
            actions = null;
            return;
        }

        IDbData _data;
        private Dictionary<string, Dictionary<string,Action<CandlestickEventArgs>>> actions;

        public void InvokeCandleStick()
        {
            var candleSticks = _data.GetData(_mangement.CurrentTick);
            if (candleSticks.Where(x=>x.Value != null).Count() == 0) return;

            foreach (var kvp in candleSticks)
            {
                if(actions.ContainsKey(kvp.Key))
                {
                    foreach (var action in actions[kvp.Key])
                    {
                        action.Value.Invoke(new CandlestickEventArgs(_mangement.CurrentTick, kvp.Value, 0, 0, true));
                    }
                }
                    //actions[kvp.Key].All(x => x.Value.Invoke(new CandlestickEventArgs(_mangement.CurrentTick, kvp.Value, 0, 0, true)));
            }
        }

        public void Subscribe(string symbol, string keyValue,Action<CandlestickEventArgs> processCandleStick)
        {
            lock (_lock)
            {
                if (actions.ContainsKey(symbol))
                    actions[symbol].Add(keyValue,processCandleStick);
                else
                {
                    _data.LoadData(SQL_STREAM_QUERY, _mangement.CurrentTick, _mangement.FinalTick, symbol, 0);
                    
                    actions.Add(symbol, new Dictionary<string, Action<CandlestickEventArgs>>() { { keyValue, processCandleStick } });
                }

                if (actions.Count >= 1) _mangement.AddStopLimitStream(InvokeCandleStick);

                DbCandleStickManagement.PauseFlow = false;
            }
        }

        public bool IsSubscribed(string symbol, string keyValue)
        {
            if (actions.ContainsKey(symbol))
            {
                if (actions[symbol].ContainsKey(keyValue))
                    return true;
            }
            return false;
        }

        public void UnSubscribe(string symbol, string keyValue)
        {
            lock (_lock)
            {
                if (actions[symbol].Count == 1)
                {
                    actions.Remove(symbol);
                }
                else
                {
                    actions[symbol].Remove(keyValue);
                }

                if (actions.Count == 0) _mangement.RemoveStopLimitStream();
            }
        }
    }


}
