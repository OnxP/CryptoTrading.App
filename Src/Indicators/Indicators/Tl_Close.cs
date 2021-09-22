using System;

namespace Tulip
{
    internal static partial class Tinet
    {
        private static int CloseStart(double[] options) => (int)options[0];

        private static int CloseStart(decimal[] options) => (int)options[0];

        private static int Close(int size, double[][] inputs, double[] options, double[][] outputs)
        {
            var period = (int)options[0];

            if (period < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= CloseStart(options))
            {
                return TI_OKAY;
            }

            double[] input = inputs[0];
            double[] output = outputs[0];

            for(int i = 0; i < period; i++)
            {
                output[i] = input[inputs[0].Length - i - 1];
            }

            return TI_OKAY;
        }

        private static int Close(int size, decimal[][] inputs, decimal[] options, decimal[][] outputs)
        {
            var period = (int)options[0];

            if (period < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= CloseStart(options))
            {
                return TI_OKAY;
            }

            decimal[] input = inputs[0];
            decimal[] output = outputs[0];

            for (int i = 0; i <= period; i++)
            {
                output[i] = input[inputs[0].Length - i - 1];
            }

            return TI_OKAY;
        }
    }
}
