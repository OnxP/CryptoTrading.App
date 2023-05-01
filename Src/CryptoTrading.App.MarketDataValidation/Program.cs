using System;
using System.Collections.Generic;
using System.IO;
using CryptoTrading.App.Core.Database;
using Microsoft.Extensions.Configuration;
using System.Data.Common;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Sockets;
using Binance;
using CryptoTrading.App.Core;

namespace CryptoTrading.App.MarketDataValidation
{
    internal class Program
    {
        public static CryptoDbContext context = new CryptoDbContext();
        static void Main(string[] args)
        {
            //inital load from the database to loop over the intervals and symbols.
            DbProviderFactories.RegisterFactory("System.Data.SqlClient", System.Data.SqlClient.SqlClientFactory.Instance);
            Database.SetInitializer<CryptoDbContext>(null);
            var Configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", true, false)
                .AddUserSecrets<Program>() // for access to API key and secret.
                .Build();

            var conn = new SqlConnection(
                @"Data Source = localhost; Initial Catalog = CryptoDb; Integrated Security = True");
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "select symbol,interval from [dbo].[CandleStickDbs] group by symbol,interval";
            var res = cmd.ExecuteReader();
            var validationList = new List<Tuple<string, int>>();
            while (res.Read())
            {
                validationList.Add(new Tuple<string, int>(res[0].ToString(),(int)res[1]));
            }
            cmd.Dispose();
            conn.Close();

            //now loop through each symbol
            List<Tuple<string, int, DateTime>> missingCandleSticks = new List<Tuple<string, int, DateTime>>();

            foreach (Tuple<string, int> SymbolInterval in validationList)
            {
                var candlesticks = context.CandleSticks.SqlQuery($"Select * from dbo.CandleStickDbs where symbol=@symbol and interval={SymbolInterval.Item2} order by Opentime",new SqlParameter("@symbol", SymbolInterval.Item1)).OrderBy(x => x.OpenTime).ToList();
                DateTime openTime = candlesticks.First().OpenTime;
                bool first = true;
                foreach (var candlestick in candlesticks)
                {
                    if (first)
                    {
                        first = false;
                        continue;
                    }
                    var nextOpenTime = CandleStickIntervalHelper.NextCandleStickTime(openTime, candlestick.Interval);

                    if (nextOpenTime == candlestick.OpenTime)
                    {
                        openTime = candlestick.OpenTime;
                        continue;
                    }
                    missingCandleSticks.Add(new Tuple<string, int, DateTime>(candlestick.Symbol,SymbolInterval.Item2,candlestick.OpenTime));
                    openTime = nextOpenTime;
                    Console.WriteLine($"{SymbolInterval.Item1} {SymbolInterval.Item2} {nextOpenTime} {candlestick.OpenTime}");
                }
                Console.WriteLine(missingCandleSticks.Count());
            }
        }
    }
}
