using CryptoTrading.App.Core.Database;
using System.Collections.Generic;
using CryptoTrading.App.Core.Database.Indicators;
using System.Linq;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Database.RunIndicators.Indicators;

namespace CryptoTraading.App.RunIndicators
{
    class Program
    {
        static void Main(string[] args)
        {
            List<string> symbols;
            //load candlesticks from database...maybe stream them into the system.
            using (var context = new CryptoDbContext())
            {
                symbols = context.CandleSticks.Select(x => x.Symbol).Distinct().ToList();
            }


            foreach (var symbol in symbols)
            {
                List<CandleInterval> intervals;
                //load candlesticks from database...maybe stream them into the system.
                using (var context = new CryptoDbContext())
                {
                    intervals = context.CandleSticks.Where(x => x.Symbol == symbol).Select(x => x.Interval).Distinct().ToList();
                }
                foreach (var interval in intervals)
                {
                    List<CandleStickDb> candlesticks;
                    using (var context = new CryptoDbContext())
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
            //list.Add(new AdxIndicator(14));
            //list.Add(new MacdIndicator(6, 13, 6));
            //list.Add(new MacdIndicator(12, 26, 9));
            //list.Add(new WacdIndicator(6, 13, 6));
            //list.Add(new WacdIndicator(12, 26, 9));
            //list.Add(new RsiIndicator(14));
            //list.Add(new StochRsi2Indicator(14,14,3,3));
            //list.Add(new StochRsiIndicator(14));
            //list.Add(new AoIndicator(1));
            //list.Add(new AroonIndicator(14));
            //list.Add(new AroonOscIndicator(14));
            //list.Add(new BbandsIndicator(20,2));
            list.Add(new DcIndicator(20));
            //list.Add(new CviIndicator(20));
            //list.Add(new PsarIndicator(0.02m,0.2m));
            //list.Add(new StddevIndicator(5));
            //list.Add(new Vsapindicator(3));
            //list.Add(new Vsapindicator(7));
            //list.Add(new VwmaIndicator(20));
            //list.Add(new WadIndicator(1));

            //list.Add(new EmaIndicator(200));
            //list.Add(new EmaIndicator(100));
            //list.Add(new EmaIndicator(50));
            //list.Add(new EmaIndicator(25));
            //list.Add(new EmaIndicator(9));

            //list.Add(new SmaIndicator(200));
            //list.Add(new SmaIndicator(100));
            //list.Add(new SmaIndicator(50));
            //list.Add(new SmaIndicator(25));
            //list.Add(new SmaIndicator(9));

            //list.Add(new HmaIndicator(200));
            //list.Add(new HmaIndicator(100));
            //list.Add(new HmaIndicator(50));
            //list.Add(new HmaIndicator(25));
            //list.Add(new HmaIndicator(9));

            //list.Add(new KamaIndicator(200));
            //list.Add(new KamaIndicator(100));
            //list.Add(new KamaIndicator(50));
            //list.Add(new KamaIndicator(25));
            //list.Add(new KamaIndicator(9));

            //list.Add(new WmaIndicator(200));
            //list.Add(new WmaIndicator(100));
            //list.Add(new WmaIndicator(50));
            //list.Add(new WmaIndicator(25));
            //list.Add(new WmaIndicator(9));

            //list.Add(new ZlEmaIndicator(200));
            //list.Add(new ZlEmaIndicator(100));
            //list.Add(new ZlEmaIndicator(50));
            //list.Add(new ZlEmaIndicator(25));
            //list.Add(new ZlEmaIndicator(9));


            return list;
        }
    }
}
