namespace Tulip
{
    internal static partial class Tinet
    {
        private static int VwapStart(double[] options) => (int)options[0] - 1;

        private static int VwapStart(decimal[] options) => (int)options[0] - 1;

        private static int Vwap(int size, double[][] inputs, double[] options, double[][] outputs)
        {
            var period = (int)options[0];

            if (period < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= VwapStart(options))
            {
                return TI_OKAY;
            }

            double[] input = inputs[0];
            double[] volume = inputs[1];
            double[] output = outputs[0];

            double sum = default;
            double vSum = default;
            int outputIndex = default;

            for (var i = 0; i < period; ++i)
            {
                sum += input[i] * volume[i];
                vSum += volume[i];
                output[outputIndex++] = sum / vSum;
            }

            return TI_OKAY;
        }

        private static int Vwap(int size, decimal[][] inputs, decimal[] options, decimal[][] outputs)
        {
            var period = (int)options[0];

            if (period < 1)
            {
                return TI_INVALID_OPTION;
            }

            if (size <= VwapStart(options))
            {
                return TI_OKAY;
            }

            decimal[] input = inputs[0];
            decimal[] volume = inputs[1];
            decimal[] output = outputs[0];

            decimal sum = default;
            decimal vSum = default;
            int outputIndex = default;

            for (var i = 0; i < period; ++i)
            {
                sum += input[i] * volume[i];
                vSum += volume[i];
                output[outputIndex++] = sum / vSum;
            }

            return TI_OKAY;
        }
    }
}
