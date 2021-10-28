using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics.X86;

namespace Tulip
{
    internal static partial class Tinet
    {
        private static int GemaStart(double[] options) => 0;

        private static int GemaStart(decimal[] options) => 0;

        private static int Gema(int size, double[][] inputs, double[] options, double[][] outputs)
        {
            var period = (int)options[0];
            var length = (int)options[1];

            if (period < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= GemaStart(options))
            {
                return TI_OKAY;
            }

            double[] input = inputs[0];
            double[] output = outputs[0];
            List<double> gema = new List<double>();
            double per = 2.0 / (period + 1);
            double ema = input[0];
            double emaPrev = input[0];
            double gradient = input[0];
            int outputIndex = default;
            output[outputIndex++] = gradient;
            for (var i = 1; i < size; ++i)
            {
                emaPrev = ema;
                ema = (input[i] - ema) * per + ema;
                gema.Add(ema - emaPrev);
                if(i>length)
                {
                    gradient = gema.Skip(i - length).Take(length).Average();
                }
                output[outputIndex++] = gradient;
            }

            return TI_OKAY;
        }

        private static int Gema(int size, decimal[][] inputs, decimal[] options, decimal[][] outputs)
        {
            var period = (int)options[0];
            var length = (int)options[1];


            if (period < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= GemaStart(options))
            {
                return TI_OKAY;
            }

            decimal[] input = inputs[0];
            decimal[] output = outputs[0];
            List<decimal> gema = new List<decimal>();

            decimal per = 2m / (period + 1);
            decimal ema = input[0];
            decimal emaPrev = input[0];
            decimal gradient = input[0];
            int outputIndex = default;
            output[outputIndex++] = gradient;

            for (var i = 1; i < size; ++i)
            {
                emaPrev = ema;
                ema = (input[i] - ema) * per + ema;
                gema.Add(ema - emaPrev);
                if (i > length)
                {
                    gradient = gema.Skip(i - length).Take(length).Average();
                }
                output[outputIndex++] = gradient;
            }

            return TI_OKAY;
        }
    }
}
