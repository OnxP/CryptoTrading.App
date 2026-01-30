using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Binance.Utility;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.MarketData
{
    public class DbMarketData : AbstractMarketData, IMarketData
    {
        ICandleStickManagement _mangement;
        IDbData _data;
        private ILogger<DbMarketData> Logger { get; set; }
        public DbMarketData(ICandleStickManagement management, IDbData data)
        {
            _mangement = management;
            _data = data;
        }

        public DbMarketData(ILogger<DbMarketData> logger,ICandleStickManagement management, IDbData data,DateTime from, DateTime to) : this(management, data)
        {
            From = from;
            To = to;
            Logger = logger;
        }

        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public Dictionary<CandlestickInterval, int> pageNumber { get; set; } = new Dictionary<CandlestickInterval, int>();

        private CancellationToken CancellationToken { get; set; }

        private string SQL_HISTORIC_QUERY = @"
with candlestick as (SELECT [ID]
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
  WHERE OpenTime >= @p0 AND OpenTime < @p1 AND Interval=@p2
  ORDER BY OpenTime
  OFFSET @p3 ROWS
  FETCH NEXT @p4 ROWS ONLY)
select * from candlestick order by Opentime
";
        private string SQL_STREAM_QUERY = @"with candlestick as (SELECT [ID]
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
  WHERE OpenTime >= @p0 AND OpenTime <= @p1 AND Interval=@p2
  ORDER BY OpenTime
  OFFSET @p3 ROWS
  FETCH NEXT @p4 ROWS ONLY)
select * from candlestick order by Opentime
";


        public async Task StartStream(CancellationToken ct)
        {
            try
            {
                CancellationToken = ct;
                await LoadHistoricData();
                await StreamData();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine();
            }
        }

        //public Task StartStream(CancellationToken ct)
        //{
        //    CancellationToken = ct;
        //    return new Task(StartStream);
        //}

        public ITaskController GetTaskController()
        {
            var controller = new TaskController(StartStream);
            return controller;
        }

        private async Task StreamData()
        {
            _mangement.BuildTimeKeeper(From, To);

            _mangement.AddMarketStream(InvokeCandleStick);
            foreach (var interval in subscribers.Keys.Select(x => x.interval).Distinct())
            {                
                var rows = await _data.LoadData(SQL_STREAM_QUERY, _mangement.CurrentTick, To,
                subscribers.Keys.Select(x => x.symbol).ToList(), (int)interval, 0);

                pageNumber.TryAdd(interval, 0);
            }

            await _mangement.StartTimeKeeper(CancellationToken);
        }

        public async Task InvokeCandleStick()
        {
            Logger.LogDebug($"Finished Processing tick :{_mangement.CurrentTick}");
            var candleSticks = _data.GetData(_mangement.CurrentTick).Values.ToList();//gets all the data for the current tick
            if (candleSticks.Any())
            {

                foreach (var interval in candleSticks.Select(x => x.Interval).Distinct())
                {
                    candleSticks.Where(x=>x.Interval==interval).OrderByDescending(x=>x.Interval).OrderBy(x => x.Volume).ToList().ForEach
                    (x =>
                    {
                        if (!subscribers.TryGetValue((x.Symbol, x.Interval), out var list)) return;
                        foreach (var action in list)
                        {
                            action.Invoke(new CandlestickEventArgs(_mangement.CurrentTick, x, 0, 0, true));
                        }
                    });
                    await LoadNextCandleSticks(candleSticks.Select(x => x.Symbol).ToList(),interval);
                }

                _data.ClearHistoric(_mangement.PreviousTick);
            }
        }


        private async Task LoadNextCandleSticks(List<string> toList,CandlestickInterval interval)
        {
            if (_data.Count() == 0)
            {
                throw new Exception();
            }
            if(DbMarketDataHelpers.CalculateFrom(_mangement.CurrentTick, interval, 1)>_mangement.FinalTick) return;

            if (_data.CheckNextTick(DbMarketDataHelpers.CalculateFrom(_mangement.CurrentTick, interval, 1),
                    toList.FirstOrDefault(),interval)) return;

            var rows = await _data.LoadData(SQL_STREAM_QUERY, _mangement.FirstTick,_mangement.FinalTick,
                toList, (int)interval, pageNumber[interval]);
            pageNumber[interval] += 1;
        }

        private async Task LoadHistoricData()
        {
            foreach (var sub in historicDataSubscribers.Keys.Select(x => x.interval).Distinct())
            {
                await _data.LoadData(SQL_HISTORIC_QUERY,
                DbMarketDataHelpers.CalculateFrom(From, sub, -201), From,
                historicDataSubscribers.Keys.Select(x => x.symbol).Distinct().ToList(),
                (int)sub, -1);
            }
           
            foreach (var item in historicDataSubscribers)
            {
                LoadHistoricData( item.Key, From, item.Value);
            }

            _data.ClearHistoric(From);

        }
        private void LoadHistoricData((string symbol, CandlestickInterval interval) symbol, DateTime from, IList<Action<IEnumerable<Candlestick>>> callback)
        {
            try
            {
                var candleSticks = _data.GetData(symbol.symbol,symbol.interval);
                if (!candleSticks.Any()) return;
                foreach (var action in callback)
                {
                    action.Invoke(candleSticks);
                }
            }
            catch
            { }
        }

        public override void Configure(IConfig request)
        {
            
        }
    }
}
