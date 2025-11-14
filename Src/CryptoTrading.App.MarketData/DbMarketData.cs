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

        public DbMarketData(ILogger<DbMarketData> logger,ICandleStickManagement management, IDbData data,DateTime from, DateTime to,CandlestickInterval interval) : this(management, data)
        {
            From = from;
            To = to;
            Logger = logger;
            Interval = interval;
        }

        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public CandlestickInterval Interval { get; set; }

        public int TotalNumberOfRows { get; set; }
        public int RequestRows { get; set; }

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
            var rows = await _data.LoadData(SQL_STREAM_QUERY, _mangement.CurrentTick, To,
                subscribers.Keys.Select(x => x.symbol).ToList(), (int)subscribers.Keys.Select(x => x.interval).First(), RequestRows*2, 0);
            RequestRows = rows;
            TotalNumberOfRows = rows;

            await _mangement.StartTimeKeeper(CancellationToken);
        }

        public async Task InvokeCandleStick()
        {
            Logger.LogDebug($"Finished Processing tick :{_mangement.CurrentTick}");
            var candleSticks = _data.GetData(_mangement.CurrentTick, false).ToList();
            if (candleSticks.All(x => x.Value != null))
            {


                //var task = LoadNextCandleSticksTask(candleSticks.Select(x => x.Key).ToList());

                candleSticks.OrderBy(x => x.Value.Volume).ToList().ForEach
                    //c.AsParallel().WithDegreeOfParallelism(Convert.ToInt32(Math.Ceiling((Environment.ProcessorCount * 0.75) * 2.0))).ForAll
                    (x =>
                    {
                        if (!subscribers.TryGetValue((x.Value.Symbol, x.Value.Interval), out var list)) return;
                        foreach (var action in list)
                        {
                            action.Invoke(new CandlestickEventArgs(_mangement.CurrentTick, x.Value, 0, 0, true));
                        }
                    });
                await LoadNextCandleSticks(candleSticks.Select(x => x.Key).ToList());

                //await task;

                _data.ClearHistoric(_mangement.PreviousTick, false);
            }
        }


        private async Task LoadNextCandleSticks(List<string> toList)
        {
            if (_data.Count(false) == 0)
            {
                throw new Exception();
            }
            if(DbMarketDataHelpers.CalculateFrom(_mangement.CurrentTick, Interval, 1)>_mangement.FinalTick) return;

            if (_data.CheckNextTick(DbMarketDataHelpers.CalculateFrom(_mangement.CurrentTick, Interval, 1),
                    toList.First(), false)) return;

            var rows = await _data.LoadData(SQL_STREAM_QUERY, _mangement.FirstTick,_mangement.FinalTick,
                toList, (int)Interval, RequestRows, TotalNumberOfRows);
            RequestRows = rows;
            TotalNumberOfRows += rows;
        }

        private async Task LoadHistoricData()
        {
            RequestRows = await _data.LoadData(SQL_HISTORIC_QUERY,
                DbMarketDataHelpers.CalculateFrom(From, historicDataSubscribers.Keys.Select(x => x.interval).First(), -201), From,
                historicDataSubscribers.Keys.Select(x => x.symbol).ToList(),
                (int)historicDataSubscribers.Keys.Select(x => x.interval).First(), historicDataSubscribers.Count * 10000,
                0);
            foreach (var item in historicDataSubscribers)
            {
                LoadHistoricData( item.Key, From, item.Value);
            }

            _data.ClearHistoric(From, false);

        }
        private void LoadHistoricData((string symbol, CandlestickInterval interval) symbol, DateTime from, IList<Action<IEnumerable<Candlestick>>> callback)
        {
            try
            {
                //_data.LoadData(SQL_HISTORIC_QUERY, From, _mangement.FinalTick, symbol.symbol, (int)symbol.interval);

                var candleSticks = _data.GetData(symbol.symbol);
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
