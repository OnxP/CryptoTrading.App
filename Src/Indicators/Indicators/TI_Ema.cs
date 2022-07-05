namespace Tulip
{
    internal static partial class Tinet
    {
        private static int EmaStart(double[] options) => 0;

        private static int EmaStart(decimal[] options) => 0;

        private static int Ema(int size, double[][] inputs, double[] options, double[][] outputs)
        {
            var period = (int)options[0];

            if (period < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= EmaStart(options))
            {
                return TI_OKAY;
            }

            outputs[0] = emaCalc(inputs[0], outputs[0].Length, period, size);
            outputs[1] = emaCalc(inputs[1], outputs[1].Length, period, size);
            outputs[2] = emaCalc(inputs[2], outputs[2].Length, period, size);
            outputs[3] = emaCalc(inputs[3], outputs[3].Length, period, size);
            outputs[4] = emaCalc(inputs[4], outputs[4].Length, period, size);

            return TI_OKAY;
        }

        private static double[] emaCalc(double[] input,int outputsize, int period, int size)
        {
            double[] output = new double[outputsize];
            double per = 2.0 / (period + 1);
            double val = input[0];
            int outputIndex = default;
            output[outputIndex++] = val;
            for (var i = 1; i < size; ++i)
            {
                val = (input[i] - val) * per + val;
                output[outputIndex++] = val;
            }

            return output;
        }
        private static decimal[] emaCalc(decimal[] input, int outputsize, int period, int size)
        {
            decimal[] output = new decimal[outputsize];
            decimal per = 2.0m / (period + 1);
            decimal val = input[0];
            int outputIndex = default;
            output[outputIndex++] = val;
            for (var i = 1; i < size; ++i)
            {
                val = (input[i] - val) * per + val;
                output[outputIndex++] = val;
            }
            return output;

        }

        private static int Ema(int size, decimal[][] inputs, decimal[] options, decimal[][] outputs)
        {
            var period = (int)options[0];

            if (period < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= EmaStart(options))
            {
                return TI_OKAY;
            }

            outputs[0] = emaCalc(inputs[0], outputs[0].Length, period, size);
            outputs[1] = emaCalc(inputs[1], outputs[1].Length, period, size);
            outputs[2] = emaCalc(inputs[2], outputs[2].Length, period, size);
            outputs[3] = emaCalc(inputs[3], outputs[3].Length, period, size);
            outputs[4] = emaCalc(inputs[4], outputs[4].Length, period, size);

            return TI_OKAY;
        }
    }
}
