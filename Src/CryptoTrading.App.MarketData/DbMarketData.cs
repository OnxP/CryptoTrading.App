using Binance;
using Binance.Utility;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Exchange;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoTrading.App.MarketData
{
    /// <summary>
    /// Backtest market-data feed backed by the <c>CandleStickDbs</c> SQL
    /// table via <see cref="IDbData"/>. PR 5c: subscriber fan-out is now
    /// neutral (<see cref="ExchangeCandlestickEvent"/> /
    /// <see cref="ExchangeCandlestick"/>); the DB boundary (EF row shape)
    /// keeps the bundled <see cref="Candlestick"/> type for this PR and we
    /// translate at the seam via <see cref="BundledSdkBridge"/>. The EF
    /// layer migrates to neutral types in PR 5e (not scheduled for 5c).
    /// </summary>
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

        public DbMarketData(ILogger<DbMarketData> logger, ICandleStickManagement management, IDbData data, DateTime from, DateTime to) : this(management, data)
        {
            From = from;
            To = to;
            Logger = logger;
        }

        public DateTime From { get; set; }
        public DateTime To { get; set; }
        // Interval key keyed on neutral CandleInterval to match AbstractMarketData dicts.
        public Dictionary<CandleInterval, int> pageNumber { get; set; } = new Dictionary<CandleInterval, int>();

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
                // The DB column Interval stores the bundled enum's ordinal. The
                // neutral CandleInterval ordinals were defined to be 1:1 with
                // bundled CandlestickInterval so (int)neutral is safe to pass.
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
                foreach (var bundledInterval in candleSticks.Select(x => x.Interval).Distinct())
                {
                    if (bundledInterval == CandlestickInterval.Minute) continue;
                    var neutralInterval = BundledSdkBridge.ToNeutralInterval(bundledInterval);

                    candleSticks.Where(x => x.Interval == bundledInterval)
                        .OrderByDescending(x => x.Interval)
                        .OrderBy(x => x.Volume)
                        .ToList()
                        .ForEach(x =>
                        {
                            if (!subscribers.TryGetValue((x.Symbol, neutralInterval), out var list)) return;
                            var neutralCandle = BundledSdkBridge.ToNeutralCandle(x);
                            var evt = new ExchangeCandlestickEvent(neutralCandle, _mangement.CurrentTick);
                            foreach (var action in list)
                            {
                                action.Invoke(evt);
                            }
                        });
                    await LoadNextCandleSticks(candleSticks.Select(x => x.Symbol).ToList(), bundledInterval);
                }

                _data.ClearHistoric(_mangement.PreviousTick);
            }
        }


        private async Task LoadNextCandleSticks(List<string> toList, CandlestickInterval interval)
        {
            if (_data.Count() == 0)
            {
                throw new Exception();
            }
            if (DbMarketDataHelpers.CalculateFrom(_mangement.CurrentTick, interval, 1) > _mangement.FinalTick) return;

            if (_data.CheckNextTick(DbMarketDataHelpers.CalculateFrom(_mangement.CurrentTick, interval, 1),
                    toList.FirstOrDefault(), interval)) return;

            var neutralInterval = BundledSdkBridge.ToNeutralInterval(interval);
            var rows = await _data.LoadData(SQL_STREAM_QUERY, _mangement.FirstTick, _mangement.FinalTick,
                toList, (int)interval, pageNumber[neutralInterval]);
            pageNumber[neutralInterval] += 1;
        }

        private async Task LoadHistoricData()
        {
            foreach (var sub in historicDataSubscribers.Keys.Select(x => x.interval).Distinct())
            {
                var bundledSub = BundledSdkBridge.ToBundledInterval(sub);
                await _data.LoadData(SQL_HISTORIC_QUERY,
                    DbMarketDataHelpers.CalculateFrom(From, bundledSub, -201), From,
                    historicDataSubscribers.Keys.Select(x => x.symbol).Distinct().ToList(),
                    (int)sub, -1);
            }

            foreach (var item in historicDataSubscribers)
            {
                LoadHistoricData(item.Key, From, item.Value);
            }

            _data.ClearHistoric(From);

        }
        private void LoadHistoricData((string symbol, CandleInterval interval) symbol, DateTime from, IList<Action<IEnumerable<ExchangeCandlestick>>> callback)
        {
            try
            {
                var bundledInterval = BundledSdkBridge.ToBundledInterval(symbol.interval);
                var bundledCandles = _data.GetData(symbol.symbol, bundledInterval);
                if (!bundledCandles.Any()) return;
                var neutralCandles = bundledCandles.Select(BundledSdkBridge.ToNeutralCandle).ToList();
                foreach (var action in callback)
                {
                    action.Invoke(neutralCandles);
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
