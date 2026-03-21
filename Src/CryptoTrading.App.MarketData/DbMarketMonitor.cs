using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Trade;
using System;
using System.Collections.Generic;
using System.Linq;
using CryptoTrading.App.Core.MarketMonitorFactory;
using System.Threading.Tasks;

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
        IDbData _data;
        private Dictionary<string, Dictionary<string, Action<ExchangeCandlestickEvent>>> actions;
        public DbMarketMonitor(ICandleStickManagement management, IDbData data)
        {
            _mangement = management;
            _data = data;
            actions = new Dictionary<string, Dictionary<string,Action<ExchangeCandlestickEvent>>>();
            //_mangement.AddMonitor(this);
        }

        public CandleInterval Interval { get; set; }
        public int NumberOfRows { get; set; } = 30;
        public int OffSet => 0 ;

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
  WHERE OpenTime > @p0 AND OpenTime <= @p1 AND Symbol in ('@Symbols') AND Interval=@p2
  ORDER BY OpenTime
  OFFSET @p3 ROWS
  FETCH NEXT @p4 ROWS ONLY";
        private readonly object _lock = new object();
        private readonly object _lockAction = new object();
        public async Task<bool> CheckOrder(ITransaction transaction)
        {
            transaction.Complete();
            return true;
        }

        public void Dispose()
        {
            actions = null;
            return;
        }

        public void InvokeCandleStick()
        {
            var candleSticks = _data.GetData(_mangement.CurrentTick, true).ToList();
            if (candleSticks.All(x => x.Value == null)) return;

            foreach (var kvp in candleSticks)
            {
                if (!actions.ContainsKey(kvp.Key)) continue;
                foreach (var action in actions[kvp.Key])
                {
                    action.Value.Invoke(new ExchangeCandlestickEvent { EventTime = _mangement.CurrentTick, Candlestick = kvp.Value, FirstTradeId = 0, LastTradeId = 0, IsFinal = true });

                    if (!_data.CheckNextTick(_mangement.NextTick, kvp.Key, true))
                    {
                        _data.LoadData(SQL_STREAM_QUERY, _mangement.CurrentTick, _mangement.FinalTick, new List<string>() { kvp.Key }, 0, NumberOfRows, OffSet);
                    }
                }
            }
            _data.ClearHistoric(_mangement.PreviousTick, true);
        }

        public void Subscribe(string symbol, string keyValue,Action<ExchangeCandlestickEvent> processCandleStick)
        {
            lock (_lockAction)
            {
                if (actions.ContainsKey(symbol))
                    actions[symbol].Add(keyValue,processCandleStick);
                else
                {
                    _data.LoadData(SQL_STREAM_QUERY, _mangement.CurrentTick, _mangement.FinalTick, new List<string>() { symbol}, 0,NumberOfRows, OffSet);
                    actions.Add(symbol, new Dictionary<string, Action<ExchangeCandlestickEvent>>() { { keyValue, processCandleStick } });
                }

                _data.ClearHistoric(_mangement.PreviousTick, true);

                if (actions.Count >= 1) _mangement.AddStopLimitStream(InvokeCandleStick);

                DbCandleStickManagement.PauseFlow = false;
            }
        }

        public bool IsSubscribed(string symbol, string keyValue)
        {
            return actions.ContainsKey(symbol) && actions[symbol].ContainsKey(keyValue);
        }

        public void UnSubscribe(string symbol, string keyValue)
        {
            lock (_lockAction)
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
