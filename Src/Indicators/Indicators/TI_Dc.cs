using System;
using System.Linq;

namespace Tulip
{
    internal static partial class Tinet
    {
        private static int DcStart(double[] options) => (int)options[0] - 1;

        private static int DcStart(decimal[] options) => (int)options[0] - 1;

        private static int Dc(int size, double[][] inputs, double[] options, double[][] outputs)
        {
            var period = (int)options[0];

            if (period < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= DcStart(options))
            {
                return TI_OKAY;
            }

            double[] high = inputs[2];
            double[] low = inputs[3];
            double[] lower = outputs[0];
            double[] middle = outputs[1];
            double[] upper = outputs[2];

            int lowerIndex = default;
            int middleIndex = default;
            int upperIndex = default;

            double lowerVal = default;
            double upperVal = default;
            for (var i = period; i < size; ++i)
            {
                upperVal = high.Skip(i - period).Take(period).Max();
                lowerVal = low.Skip(i - period).Take(period).Min();

                upper[upperIndex++] = upperVal;
                lower[lowerIndex++] = lowerVal;
                middle[middleIndex++] = (upperVal + lowerVal) / 2.0;
                
            }

            return TI_OKAY;
        }

        private static int Dc(int size, decimal[][] inputs, decimal[] options, decimal[][] outputs)
        {
            var period = (int)options[0];

            if (period < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= DcStart(options))
            {
                return TI_OKAY;
            }

            decimal[] high = inputs[2];
            decimal[] low = inputs[3];
            decimal[] lower = outputs[0];
            decimal[] middle = outputs[1];
            decimal[] upper = outputs[2];

            int lowerIndex = default;
            int middleIndex = default;
            int upperIndex = default;

            decimal lowerVal = default;
            decimal upperVal = default;
            for (var i = period; i < size; ++i)
            {
                upperVal = high.Skip(i - period).Take(period).Max();
                lowerVal = low.Skip(i - period).Take(period).Min();

                upper[upperIndex++] = upperVal;
                lower[lowerIndex++] = lowerVal;
                middle[middleIndex++] = (upperVal + lowerVal) / 2m;
            }

            return TI_OKAY;
        }
    }
}
