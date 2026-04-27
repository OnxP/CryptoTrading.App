using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Trade;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.MarketData
{
    /// <summary>
    /// DB-backed monitor for backtests. PR 5h: the EF row layer surfaces
    /// neutral <see cref="ExchangeCandlestick"/> values directly, so this
    /// class is bundled-SDK-free end-to-end.
    /// </summary>
    public class DbMarketMonitor : IMarketMonitor
    {
        ICandleStickManagement _mangement;
        IDbData _data;
        private Dictionary<string, Dictionary<string, Action<ExchangeCandlestickEvent>>> actions;
        public DbMarketMonitor(ICandleStickManagement management, IDbData data)
        {
            _mangement = management;
            _data = data;
            actions = new Dictionary<string, Dictionary<string, Action<ExchangeCandlestickEvent>>>();
        }

        public CandleInterval Interval { get; set; }
        public int pageNumber { get; set; } = 0;

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
        private string SQL_HISTORIC_QUERY = @"SELECT [ID]
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

        public async Task InvokeCandleStick()
        {
            var candleSticks = _data.GetData(_mangement.CurrentTick).Where(x => x.Key.Item2 == CandleInterval.Minute_1).ToList();
            if (candleSticks.All(x => x.Value == null)) return;

            foreach (var kvp in candleSticks)
            {
                if (!actions.ContainsKey(kvp.Key.Item1)) continue;
                var evt = new ExchangeCandlestickEvent(kvp.Value, _mangement.CurrentTick);
                foreach (var action in actions[kvp.Key.Item1])
                {
                    action.Value.Invoke(evt);

                    if (!_data.CheckNextTick(_mangement.NextTick, kvp.Key.Item1, kvp.Key.Item2))
                    {
                        await _data.LoadData(SQL_STREAM_QUERY, _mangement.CurrentTick, _mangement.FinalTick,
                            new List<string>() { kvp.Key.Item1 }, 0, 0);
                    }
                }
            }
            // Clear current tick's data — it's been fully consumed by both
            // DbMarketData (4H/15M) and DbMarketMonitor (1M) at this point
            _data.ClearHistoric(_mangement.CurrentTick);
        }

        public async Task<List<ExchangeCandlestick>> GetHistoricCandleSticks(string symbol)
        {
            var rows = await _data.LoadData(SQL_HISTORIC_QUERY,
                DbMarketDataHelpers.CalculateFrom(_mangement.CurrentTick, CandleInterval.Minute_1, -201), _mangement.CurrentTick,
                [symbol], 0, -1);
            return _data.GetData(symbol, CandleInterval.Minute_1);
        }

        public async Task Subscribe(string symbol, string keyValue, Action<ExchangeCandlestickEvent> processCandleStick)
        {
            if (actions.ContainsKey(symbol))
                actions[symbol].Add(keyValue, processCandleStick);
            else
            {
                var rows = await _data.LoadData(SQL_STREAM_QUERY, _mangement.CurrentTick, _mangement.FinalTick, new List<string>() { symbol }, 0, pageNumber);
                pageNumber++;
                actions.Add(symbol, new Dictionary<string, Action<ExchangeCandlestickEvent>>() { { keyValue, processCandleStick } });
            }

            _data.ClearHistoric(_mangement.PreviousTick);

            if (actions.Count >= 1) _mangement.AddStopLimitStream(InvokeCandleStick);
        }

        public bool IsSubscribed(string symbol, string keyValue)
        {
            return actions.ContainsKey(symbol) && actions[symbol].ContainsKey(keyValue);
        }

        public void UnSubscribe(string symbol, string keyValue)
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
