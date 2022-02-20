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
        IDbData _data;
        
        public DbMarketData(ICandleStickManagement management, IDbData data)
        {
            _mangement = management;
            _data = data;
        }

        public DateTime From { get; set; }
        public DateTime To { get; set; }

        public int NumberOfRows=>100*subscribers.Keys.Count;
        public int NoOfCalls { get; set; } = 0;
        public int OffSet => NumberOfRows * NoOfCalls;

        private string SQL_HISTORIC_QUERY = @"
SELECT DISTINCT [ID]
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
  WHERE CloseTime >= @p0 AND CloseTime < @p1 AND Symbol in ('@Symbols') AND Interval=@p2
  ORDER BY CloseTime
  OFFSET @p3 ROWS
  FETCH NEXT @p4 ROWS ONLY
";
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
  WHERE CloseTime >= @p0 AND CloseTime <= @p1 AND Symbol in ('@Symbols') AND Interval=@p2
  ORDER BY CloseTime
  OFFSET @p3 ROWS
  FETCH NEXT @p4 ROWS ONLY
";

        public override void Configure(IRequest request)
        {
            //context = new CryptoDbContext();
        }

        public void Configure(IConfig request)
        {
            throw new NotImplementedException();
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
            //foreach (var item in subscribers)
            //{
            //    _data.LoadData(SQL_STREAM_QUERY, From, To, item.Key.symbol, (int)item.Key.interval);
            //}
            _mangement.BuildTimeKeeper(From, To);

            _mangement.AddMarketStream(InvokeCandleStick);
            _data.LoadData(SQL_STREAM_QUERY, _mangement.CurrentTick, To,
                subscribers.Keys.Select(x => x.symbol).ToList(), (int)subscribers.Keys.Select(x => x.interval).First(), NumberOfRows, OffSet);
            NoOfCalls++;
            //_data.Initialise(From, To, subscribers.Keys.Select(x => x.symbol).ToList(),
            //    subscribers.Keys.Select(x => x.interval).First());

            _mangement.StartTimeKeeper();
        }

        public void InvokeCandleStick()
        {
            var candleSticks = _data.GetData(_mangement.CurrentTick, false).ToList();
            if (candleSticks.All(x => x.Value == null))
            {
                return;
            }

            //var task = LoadNextCandleSticksTask(candleSticks.Select(x => x.Key).ToList());

            candleSticks.OrderBy(x => x.Value.Volume).AsParallel()
                .WithDegreeOfParallelism(Convert.ToInt32(Math.Ceiling((Environment.ProcessorCount * 0.75) * 2.0)))
                .ForAll(x =>
                {
                    if (!subscribers.TryGetValue((x.Value.Symbol, x.Value.Interval), out var list)) return;
                    foreach (var action in list)
                    {
                        action.Invoke(new CandlestickEventArgs(_mangement.CurrentTick, x.Value, 0, 0, true));
                    }
                });
            LoadNextCandleSticks(candleSticks.Select(x => x.Key).ToList());

            //await task;

            _data.ClearHistoric(_mangement.CurrentTick, false);

            while (DbCandleStickManagement.PauseFlow)
            {
                //pause the flow for execution, specific to db use only.
            }
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
            if (!_data.CheckNextTick(CalculateFrom(_mangement.CurrentTick, CandlestickInterval.Minutes_15, 1), toList.First(), false))
            {
                _data.LoadData(SQL_STREAM_QUERY, _mangement.FirstTick,_mangement.FinalTick,
                    toList, 3,NumberOfRows,OffSet);
                NoOfCalls++;
            }
        }

        private DateTime CalculateFrom(DateTime dateTime, CandlestickInterval interval,int NoOfCandleSticks)
        {
            int candleSticksToLoad = -1 * NoOfCandleSticks;
            return interval switch
            {
                CandlestickInterval.Minute => dateTime.AddMinutes(-1 * candleSticksToLoad),
                CandlestickInterval.Minutes_3 => dateTime.AddMinutes(-3 * candleSticksToLoad),
                CandlestickInterval.Minutes_5 => dateTime.AddMinutes(-5 * candleSticksToLoad),
                CandlestickInterval.Minutes_15 => dateTime.AddMinutes(-15 * candleSticksToLoad),
                CandlestickInterval.Minutes_30 => dateTime.AddMinutes(-30 * candleSticksToLoad),
                CandlestickInterval.Hour => dateTime.AddHours(-1 * candleSticksToLoad),
                CandlestickInterval.Hours_2 => dateTime.AddHours(-2 * candleSticksToLoad),
                CandlestickInterval.Hours_4 => dateTime.AddHours(-4 * candleSticksToLoad),
                CandlestickInterval.Hours_6 => dateTime.AddHours(-6 * candleSticksToLoad),
                CandlestickInterval.Hours_8 => dateTime.AddHours(-8 * candleSticksToLoad),
                CandlestickInterval.Hours_12 => dateTime.AddHours(-12 * candleSticksToLoad),
                CandlestickInterval.Day => dateTime.AddDays(-1 * candleSticksToLoad),
                CandlestickInterval.Days_3 => dateTime.AddDays(-3 * candleSticksToLoad),
                CandlestickInterval.Week => dateTime.AddDays(-7 * candleSticksToLoad),
                CandlestickInterval.Month => dateTime.AddMonths(-1 * candleSticksToLoad),
                _ => dateTime,
            };
        }
        private void LoadHistoricData()
        {
            _data.LoadData(SQL_HISTORIC_QUERY, CalculateFrom(From, historicDataSubscribers.Keys.Select(x => x.interval).First(),-200), From,
                historicDataSubscribers.Keys.Select(x => x.symbol).ToList(), (int)historicDataSubscribers.Keys.Select(x => x.interval).First(),NumberOfRows*1000,OffSet);
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
    }
}
