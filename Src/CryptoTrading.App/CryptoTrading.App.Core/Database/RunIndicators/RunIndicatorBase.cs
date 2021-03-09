using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text;
using Tulip;

namespace CryptoTrading.App.Core.Database.Indicators
{
    public abstract class RunIndicatorBase<T,C> : IExecute where T : DbContext, new()
    {
        public T context { get; set; }

        public List<C> list { get; set; }

        public RunIndicatorBase(params decimal[] option)
        {
            context = new T();
            list = new List<C>();
            options = option;
        }

        protected decimal[] options { get; set; }

        public DbContext Context => context;

        protected abstract C AddToDb(int candlestickId, params decimal[] outputs);

        public void Execute(List<CandleStickDb> candleStick)
        {
            var indicator = Tulip.Indicators.adx;
            decimal[] close_prices = candleStick.Select(x => (decimal)x.Close).ToArray();
            decimal[] volume = candleStick.Select(x => (decimal)x.Volume).ToArray();
            decimal[] high = candleStick.Select(x => (decimal)x.High).ToArray();
            decimal[] low = candleStick.Select(x => (decimal)x.Low).ToArray();

            //Find output size and allocate output space.
            int output_length = close_prices.Length - indicator.Start(options);

            decimal[] output = new decimal[output_length];
            decimal[] output1 = new decimal[output_length];
            decimal[] output2 = new decimal[output_length];

            decimal[][] inputs = { close_prices, volume, high, low };
            decimal[][] outputs = { output, output1, output2 };
            int success = indicator.Run(inputs, options, outputs);

            candleStick.Reverse();
            int j = 0;
            for (int i = outputs[0].Length - 1; i >= 0; i--)
            {
                list.Add(AddToDb(candleStick.Skip(j++).Select(x => x.ID).First(),outputs[0][i], outputs[1][i], outputs[2][i]));
            }

            SaveContext();
        }

        protected abstract void SaveContext();

        public void Dispose()
        {
            context.Dispose();

            
        }

    }
}
