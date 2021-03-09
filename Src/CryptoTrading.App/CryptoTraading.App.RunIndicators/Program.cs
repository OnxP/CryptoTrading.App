using CryptoTrading.App.Core.Database;
using System;
using System.Collections.Generic;
using CryptoTrading.App.Core.Database.Indicators;
using System.Linq;
using Binance;
using CryptoTrading.App.Core.Database.RunIndicators.Indicators;

namespace CryptoTraading.App.RunIndicators
{
    class Program
    {
        static void Main(string[] args)
        {
            List<string> symbols;
            //load candlesticks from database...maybe stream them into the system.
            using (var context = new CryptoDBContext())
            {
                symbols = context.CandleSticks.Select(x => x.Symbol).Distinct().ToList();
            }

            foreach (var symbol in symbols)
            {
                List<CandlestickInterval> intervals;
                //load candlesticks from database...maybe stream them into the system.
                using (var context = new CryptoDBContext())
                {
                    intervals = context.CandleSticks.Where(x=>x.Symbol == symbol).Select(x => x.Interval).Distinct().ToList();
                }
                foreach (var interval in intervals)
                {
                    List<CandleStickDb> candlesticks;
                    using (var context = new CryptoDBContext())
                    {
                        candlesticks = context.CandleSticks.Where(x => x.Symbol == symbol && x.Interval == interval).OrderBy(x => x.OpenTime).ToList();
                    }
                    var indiicators = CreateIndicators();
                    indiicators.ForEach(x => x.Execute(candlesticks));
                    indiicators.ForEach(x => x.Context.BatchSaveChanges());
                    indiicators.ForEach(x => x.Dispose());
                }
            }
            
        }

        private static List<IExecute> CreateIndicators()
        {
            var list = new List<IExecute>();
            list.Add(new AdxIndicator(14));


            return list;
        }
    }
}
