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

            double[] close = inputs[0];
            double[] volume = inputs[1];
            double[] high = inputs[2];
            double[] low = inputs[3];

            for (int i = 0; i < period; i++)
            {
                outputs[0][i] = close[inputs[0].Length - i - 1];
                outputs[1][i] = volume[inputs[1].Length - i - 1];
                outputs[2][i] = high[inputs[2].Length - i - 1];
                outputs[3][i] = low[inputs[3].Length - i - 1];
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
