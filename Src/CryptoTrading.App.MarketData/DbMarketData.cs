using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.TradeRequest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        public DbMarketData(ILogger<DbMarketData> logger,ICandleStickManagement management, IDbData data,DateTime from, DateTime to,CandleInterval interval) : this(management, data)
        {
            From = from;
            To = to;
            Logger = logger;
            Interval = interval;
        }

        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public CandleInterval Interval { get; set; }

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
                LoadHistoricData();
                StreamData();
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

        private void StreamData()
        {
            _mangement.BuildTimeKeeper(From, To);

            _mangement.AddMarketStream(InvokeCandleStick);
            var rows = _data.LoadData(SQL_STREAM_QUERY, _mangement.CurrentTick, To,
                subscribers.Keys.Select(x => x.symbol).ToList(), (int)subscribers.Keys.Select(x => x.interval).First(), RequestRows*2, 0);
            RequestRows = rows;
            TotalNumberOfRows = rows;

            _mangement.StartTimeKeeper(CancellationToken);
        }

        public void InvokeCandleStick()
        {
            Logger.LogDebug($"Finished Processing tick :{_mangement.CurrentTick}");
            var candleSticks = _data.GetData(_mangement.CurrentTick, false).ToList();
            if (candleSticks.All(x => x.Value == null))
            {
                return;
            }

            //var task = LoadNextCandleSticksTask(candleSticks.Select(x => x.Key).ToList());

            candleSticks.OrderBy(x => x.Value.Volume).ToList().ForEach
                //c.AsParallel().WithDegreeOfParallelism(Convert.ToInt32(Math.Ceiling((Environment.ProcessorCount * 0.75) * 2.0))).ForAll
                (x =>
                {
                    if (!subscribers.TryGetValue((x.Value.Symbol, x.Value.Interval), out var list)) return;
                    foreach (var action in list)
                    {
                        action.Invoke(new ExchangeCandlestickEvent { EventTime = _mangement.CurrentTick, Candlestick = x.Value, FirstTradeId = 0, LastTradeId = 0, IsFinal = true });
                    }
                });
            LoadNextCandleSticks(candleSticks.Select(x => x.Key).ToList());

            //await task;

            _data.ClearHistoric(_mangement.PreviousTick, false);
        }

        private async Task LoadNextCandleSticksTask(List<string> toList)
        {
            await Task.Run(() =>
            {
                LoadNextCandleSticks(toList);
            });
        }

        private void LoadNextCandleSticks(List<string> toList)
        {
            if (_data.Count(false) == 0)
            {
                throw new Exception();
            }
            if(CalculateFrom(_mangement.CurrentTick, Interval, 1)>_mangement.FinalTick) return;

            if (_data.CheckNextTick(CalculateFrom(_mangement.CurrentTick, Interval, 1),
                    toList.First(), false)) return;

            var rows = _data.LoadData(SQL_STREAM_QUERY, _mangement.FirstTick,_mangement.FinalTick,
                toList, (int)Interval, RequestRows, TotalNumberOfRows);
            RequestRows = rows;
            TotalNumberOfRows += rows;
        }

        private DateTime CalculateFrom(DateTime dateTime, CandleInterval interval,int NoOfCandleSticks)
        {
            int candleSticksToLoad = -1 * NoOfCandleSticks;
            return interval switch
            {
                CandleInterval.Minute_1 => dateTime.AddMinutes(-1 * candleSticksToLoad),
                CandleInterval.Minute_3 => dateTime.AddMinutes(-3 * candleSticksToLoad),
                CandleInterval.Minute_5 => dateTime.AddMinutes(-5 * candleSticksToLoad),
                CandleInterval.Minute_15 => dateTime.AddMinutes(-15 * candleSticksToLoad),
                CandleInterval.Minute_30 => dateTime.AddMinutes(-30 * candleSticksToLoad),
                CandleInterval.Hour_1 => dateTime.AddHours(-1 * candleSticksToLoad),
                CandleInterval.Hour_2 => dateTime.AddHours(-2 * candleSticksToLoad),
                CandleInterval.Hour_4 => dateTime.AddHours(-4 * candleSticksToLoad),
                CandleInterval.Hour_6 => dateTime.AddHours(-6 * candleSticksToLoad),
                CandleInterval.Hour_8 => dateTime.AddHours(-8 * candleSticksToLoad),
                CandleInterval.Hour_12 => dateTime.AddHours(-12 * candleSticksToLoad),
                CandleInterval.Day_1 => dateTime.AddDays(-1 * candleSticksToLoad),
                CandleInterval.Day_3 => dateTime.AddDays(-3 * candleSticksToLoad),
                CandleInterval.Week_1 => dateTime.AddDays(-7 * candleSticksToLoad),
                CandleInterval.Month_1 => dateTime.AddMonths(-1 * candleSticksToLoad),
                _ => dateTime,
            };
        }
        private void LoadHistoricData()
        {
            RequestRows = _data.LoadData(SQL_HISTORIC_QUERY,
                CalculateFrom(From, historicDataSubscribers.Keys.Select(x => x.interval).First(), -201), From,
                historicDataSubscribers.Keys.Select(x => x.symbol).ToList(),
                (int)historicDataSubscribers.Keys.Select(x => x.interval).First(), historicDataSubscribers.Count * 10000,
                0);
            foreach (var item in historicDataSubscribers)
            {
                LoadHistoricData( item.Key, From, item.Value);
            }

            _data.ClearHistoric(From, false);

        }
        private void LoadHistoricData((string symbol, CandleInterval interval) symbol, DateTime from, IList<Action<IEnumerable<ExchangeCandlestick>>> callback)
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
